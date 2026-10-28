#!/usr/bin/env python3
"""Provision only a newly created, task-owned PostgreSQL container. Never pulls images."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
import time
import uuid

REPO = Path(__file__).resolve().parents[2]
LABEL = 'electrolink.m33.task'


def docker(*args, stdin=None):
    result = subprocess.run(['docker', *args], input=stdin, text=True, capture_output=True)
    if result.returncode:
        # Docker errors can echo environment or arguments. Do not dump them.
        raise RuntimeError('Docker operation failed: ' + args[0])
    return result.stdout.strip()


def save(path, value):
    # Parent is exclusively created for this invocation with mode 0700.
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        json.dump(value, output, indent=2)


def verify(manifest):
    task = manifest['taskId']
    if not re.fullmatch('[0-9a-f]{32}', task) or manifest['database'] != 'electrolink_m33_' + task or manifest['host'] != '127.0.0.1':
        raise RuntimeError('Unrecognized task-owned destination')
    container = json.loads(docker('inspect', manifest['containerId']))[0]
    if container['Id'] != manifest['containerId'] or container['Config']['Labels'].get(LABEL) != task or container['Image'] != manifest['imageId']:
        raise RuntimeError('Container ownership mismatch')
    env = dict(v.split('=', 1) for v in container['Config']['Env'] if '=' in v)
    if env.get('POSTGRES_DB') != manifest['database'] or env.get('POSTGRES_PASSWORD') != manifest['password']:
        raise RuntimeError('Container database identity mismatch')
    bindings = container['HostConfig']['PortBindings']
    if set(bindings) != {'5432/tcp'} or len(bindings['5432/tcp']) != 1 or bindings['5432/tcp'][0]['HostIp'] != '127.0.0.1':
        raise RuntimeError('Port is not exclusively loopback')
    return container


def require_local_engine():
    # A remote Docker context's loopback is not this machine's loopback.
    endpoint = (docker('context', 'inspect', '--format', '{{.Endpoints.docker.Host}}')
                if os.environ.get('DOCKER_CONTEXT') else
                os.environ.get('DOCKER_HOST') or docker('context', 'inspect', '--format', '{{.Endpoints.docker.Host}}'))
    if not endpoint.startswith('unix:///'):
        raise RuntimeError('Refusing a non-local Docker engine')


def prepare(image, legacy_schema=None):
    require_local_engine()
    docker('info', '--format', '{{.ServerVersion}}')
    info = json.loads(docker('image', 'inspect', image))[0]  # inspect never pulls
    tags = info.get('RepoTags') or []
    if not any(re.fullmatch(r'(docker.io/library/)?postgres:[^/]+|kartoza/postgis:[^/]+', tag) for tag in tags):
        raise RuntimeError('Select an existing postgres or kartoza/postgis image; no download or substitute is authorized')
    task = uuid.uuid4().hex
    directory = Path(tempfile.mkdtemp(prefix='electrolink-m33-'))
    manifest_path = directory / 'manifest.json'
    manifest = dict(taskId=task, database='electrolink_m33_' + task, host='127.0.0.1', port=0,
                    password=secrets.token_hex(32), jwtSecret=secrets.token_hex(48), edgeKey=secrets.token_hex(32),
                    imageId=info['Id'], phase='allocated')
    save(manifest_path, manifest)
    environment = directory / 'postgres.env'
    environment.write_text('POSTGRES_DB=' + manifest['database'] + '\nPOSTGRES_PASSWORD=' + manifest['password'] + '\n')
    environment.chmod(0o600)
    try:
        # Bypass the image entrypoint entirely: no PostGIS extensions, cron or image jobs.
        # Start only PostgreSQL as its unprivileged OS user, with a new private cluster.
        bootstrap = '''set -eu
umask 077
printf '%s' "$POSTGRES_PASSWORD" > /tmp/m33-password
initdb -D /tmp/m33-cluster -U postgres --auth-local=trust --auth-host=scram-sha-256 --pwfile=/tmp/m33-password >/tmp/m33-initdb.log
rm /tmp/m33-password
printf '\nhost all all 0.0.0.0/0 scram-sha-256\n' >> /tmp/m33-cluster/pg_hba.conf
exec postgres -D /tmp/m33-cluster -c listen_addresses='*' -c log_statement=none -c log_min_error_statement=panic
'''
        container_id = docker('create', '--pull=never', '--name', 'electrolink-m33-' + task,
                              '--label', LABEL + '=' + task, '-p', '127.0.0.1::5432',
                              '--env-file', str(environment), '--user', 'postgres', '--entrypoint', '/bin/sh', manifest['imageId'], '-c', bootstrap)
        manifest.update(containerId=container_id, phase='created')
        save(manifest_path, manifest)
        verify(manifest)
        docker('start', container_id)
        ready = False
        for _ in range(60):
            probe = subprocess.run(['docker', 'exec', container_id, 'pg_isready', '-U', 'postgres', '-d', 'postgres'], capture_output=True)
            if probe.returncode == 0:
                ready = True
                break
            time.sleep(0.5)
        if not ready:
            raise RuntimeError('New PostgreSQL container is not ready')
        container = verify(manifest)
        published = container['NetworkSettings']['Ports']['5432/tcp']
        if len(published) != 1 or published[0]['HostIp'] != '127.0.0.1':
            raise RuntimeError('Unexpected published endpoint')
        manifest['port'] = int(published[0]['HostPort'])
        save(manifest_path, manifest)
        # Database is created only inside this just-created and verified private cluster.
        verify(manifest)
        docker('exec', container_id, 'createdb', '-U', 'postgres', manifest['database'])
        sql_args = ['exec', '-i', container_id, 'psql', '-X', '-U', 'postgres', '-d', manifest['database'], '-v', 'ON_ERROR_STOP=1', '-At']
        identity = docker(*sql_args, stdin='SELECT current_database(); SELECT count(*) FROM pg_tables WHERE schemaname NOT IN (\'pg_catalog\',\'information_schema\');')
        if identity.splitlines() != [manifest['database'], '0']:
            raise RuntimeError('Refusing SQL: destination is not the newly created empty database')
        scripts = [REPO / 'StoreManagement/Infrastructure/Persistence/Sql' / name for name in
                   ['001-store-hierarchy.draft.sql', '002-equipment-telemetry.draft.sql', '003-alert-management.draft.sql']]
        if legacy_schema is not None:
            legacy_schema = legacy_schema.resolve(strict=True)
            if not legacy_schema.is_file() or legacy_schema.suffix != '.sql':
                raise RuntimeError('Reviewed legacy schema must be an explicit SQL file')
            scripts.insert(0, legacy_schema)
        manifest['ddlSha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in scripts}
        verify(manifest)  # Check current container provenance immediately before SQL.
        sql = 'BEGIN;\nCOMMENT ON DATABASE "' + manifest['database'] + '" IS \'electrolink-m33:' + task + "';\n"
        sql += '\n'.join(p.read_text() for p in scripts) + '\nCOMMIT;\n'
        docker(*sql_args, stdin=sql)
        manifest['phase'] = 'initialized'
        save(manifest_path, manifest)
        print('Prepared NEW disposable PostgreSQL: database=' + manifest['database'] + ' endpoint=127.0.0.1:' + str(manifest['port']))
        print('Private manifest: ' + str(manifest_path))
        print('Run the opt-in .NET demonstration with this manifest; credentials must remain outside Git.')
    except Exception:
        print('Preparation incomplete. Private ownership record: ' + str(manifest_path), file=sys.stderr)
        raise
    finally:
        environment.unlink(missing_ok=True)


def stop(path):
    require_local_engine()
    manifest = json.loads(path.read_text())
    container = verify(manifest)
    if container['State']['Running']:
        docker('stop', manifest['containerId'])
    print('Task-owned container stopped; database/volumes retained. No removal performed.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    create = commands.add_parser('prepare')
    create.add_argument('--image', required=True, help='Existing authorized postgres or kartoza/postgis image tag/ID; never pulled')
    create.add_argument('--legacy-schema', type=Path, help='Explicit offline-generated and reviewed AppDbContext DDL for a NEW database only')
    halt = commands.add_parser('stop')
    halt.add_argument('manifest', type=Path)
    args = parser.parse_args()
    try:
        if args.command == 'prepare':
            prepare(args.image, args.legacy_schema)
        else:
            stop(args.manifest)
        return 0
    except Exception as error:
        print(str(error) if isinstance(error, RuntimeError) else 'Local preparation failed: ' + type(error).__name__, file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
