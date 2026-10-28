using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Hampcoders.Electrolink.API.AlertManagement.Application;
using Hampcoders.Electrolink.API.AlertManagement.Infrastructure;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Explicit opt-in executable calls this; not a skipped/passing test when PostgreSQL is absent.
public static class M33PostgresDemo
{
    public static async Task Run(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Missing manifest");
        if (!Regex.IsMatch(manifest.TaskId, "^[0-9a-f]{32}$") || manifest.Database != "electrolink_m33_" + manifest.TaskId
            || manifest.Host != "127.0.0.1" || manifest.Port is < 1024 or > 65535
            || manifest.JwtSecret.Length < 32 || manifest.EdgeKey.Length < 32 || manifest.Password.Length < 32)
            throw new InvalidOperationException("Refusing unrecognized disposable destination or missing synthetic credentials");
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = manifest.Host, Port = manifest.Port, Database = manifest.Database, Username = "postgres",
            Password = manifest.Password, Pooling = false, Timeout = 5, CommandTimeout = 15,
            IncludeErrorDetail = false
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(manifest.Database, await Scalar(connection, "SELECT current_database()"));
        Assert.Equal("electrolink-m33:" + manifest.TaskId, await Scalar(connection,
            "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname=current_database()"));
        var options = new DbContextOptionsBuilder<StoreManagementDbContext>().UseNpgsql(connectionString).Options;
        await using (var schema = new StoreManagementDbContext(options))
        {
            await CheckSchema(connection, schema);
            // Never replay the demonstration into an existing inventory, even this task's previous run.
            Assert.Equal(0, await schema.Alerts.CountAsync());
            Assert.Equal(0, await schema.Stores.CountAsync());
            Assert.Equal(0, await schema.Areas.CountAsync());
            Assert.Equal(0, await schema.Equipment.CountAsync());
            Assert.Equal(0, await schema.DeviceBindings.CountAsync());
            Assert.Equal(0, await schema.Measurements.CountAsync());
        }
        Console.WriteLine("PASS destination ownership marker; 6 tables, columns, PKs, FKs and indexes match EF; empty database");
        var data = new EquipmentTelemetryScenario(manifest.EdgeKey);
        var settings = new PostgresScenarioSettings(connectionString, manifest.JwtSecret, manifest.EdgeKey);
        string root, area, equipment;
        JsonElement acknowledgement;
        using (var h = new StoreHttpScenario(data, settings))
        {
            settings = settings with { Owner = h.Owner, Other = h.Other };
            var s = await h.Create("/api/v1/stores", new { name = "M3.3 synthetic store", address = "Synthetic address" });
            root = "/api/v1/stores/" + s.GetProperty("id").GetString();
            var a = await h.Create(root + "/areas", new { name = "Synthetic kitchen" });
            area = root + "/areas/" + a.GetProperty("id").GetString();
            var e = await h.Create(area + "/equipment", new { name = "Synthetic oven" });
            equipment = area + "/equipment/" + e.GetProperty("id").GetString();
            foreach (var path in new[] { root, area, equipment, "/api/v1/stores", root + "/areas", area + "/equipment" })
                await Status(h.Send(HttpMethod.Get, path), HttpStatusCode.OK);
            await Status(h.Send(HttpMethod.Get, equipment + "/telemetry", otherUser: true), HttpStatusCode.NotFound);
            await Status(h.Send(HttpMethod.Post, equipment + "/device-binding",
                new { deviceId = data.ForeignDevice.Id.Value, edgeDeviceId = data.ForeignDevice.SerialNumber.Value }), HttpStatusCode.Forbidden);
            await Status(h.Send(HttpMethod.Post, equipment + "/device-binding",
                new { deviceId = data.Device.Id.Value, edgeDeviceId = data.Device.SerialNumber.Value }), HttpStatusCode.Created);
            await Status(h.Send(HttpMethod.Get, equipment + "/device-binding"), HttpStatusCode.OK);
            Assert.Equal("UNKNOWN", (await State(h, equipment)).GetProperty("state").GetString());
            await Status(h.Send(HttpMethod.Get, "/api/v1/iot/edge/devices/" + data.Device.SerialNumber.Value + "/thresholds",
                anonymous: true, edgeKey: true), HttpStatusCode.OK);
            var timestamp = DateTime.UtcNow.AddSeconds(-2);
            timestamp = new DateTime(timestamp.Ticks - timestamp.Ticks % 10 + 7, DateTimeKind.Utc);
            var normal = new Payload(data.Device.SerialNumber.Value, "m33-normal", timestamp, 220, 1, 1, 60, "EDGE");
            await Status(Ingest(h, normal), HttpStatusCode.NoContent);
            await Status(Ingest(h, normal), HttpStatusCode.NoContent);
            await using (var fresh = new StoreManagementDbContext(options))
            {
                Assert.Equal(1, await fresh.Measurements.CountAsync());
                Assert.Equal(0, await fresh.Alerts.CountAsync());
                var persisted = await fresh.Measurements.AsNoTracking().SingleAsync();
                Assert.Equal(normal.ReadingId, persisted.ReadingId);
                Assert.Equal(normal.Timestamp.Ticks / 10, persisted.CapturedAtUtc.Ticks / 10); // PostgreSQL microsecond precision.
                Assert.Equal(220, persisted.Voltage);
            }
            Assert.Equal("NORMAL", (await State(h, equipment)).GetProperty("state").GetString());
            await Status(Ingest(h, normal with { Voltage = 300 }), HttpStatusCode.Conflict);
            await Status(Ingest(h, normal with { ReadingId = "m33-anomaly", Timestamp = DateTime.UtcNow.AddSeconds(-1), Current = 30, PowerFactor = 0.5f }), HttpStatusCode.NoContent);
            var anomaly = await State(h, equipment);
            Assert.Equal("ANOMALY", anomaly.GetProperty("state").GetString());
            Assert.Equal("ShortCircuitRisk", anomaly.GetProperty("lastMeasurement").GetProperty("detectedAnomalyType").GetString());
            Assert.Equal("Critical", anomaly.GetProperty("lastMeasurement").GetProperty("detectedSeverity").GetString());
            acknowledgement = await ExerciseAlerts(h, equipment, options);
        }
        Console.WriteLine("PASS HTTP hierarchy, authorized binding, UNKNOWN/NORMAL/ANOMALY, identical replay 204, changed payload 409, foreign actor and invalid binding denied");
        // Entire HTTP composition replaced; same synthetic IAM/Assets records, new contexts and database sessions.
        data.MachineScope = false;
        using (var next = new StoreHttpScenario(data, settings))
        {
            foreach (var path in new[] { root, area, equipment, equipment + "/device-binding" })
                await Status(next.Send(HttpMethod.Get, path), HttpStatusCode.OK);
            Assert.Equal("ANOMALY", (await State(next, equipment)).GetProperty("state").GetString());
            var persistedAck = await Json(next, equipment + "/alerts/" + acknowledgement.GetProperty("id").GetString());
            Assert.Equal("ACKNOWLEDGED", persistedAck.GetProperty("status").GetString());
            Assert.Equal(acknowledgement.GetProperty("acknowledgedAtUtc").GetString(), persistedAck.GetProperty("acknowledgedAtUtc").GetString());
            Assert.Equal(settings.Owner!.Id.Value, persistedAck.GetProperty("acknowledgedByUserId").GetString());
            await Status(Ingest(next, new Payload(data.Device.SerialNumber.Value, "m33-no-scope", DateTime.UtcNow.AddSeconds(-1), 220, 1, 1, 60, "EDGE")), HttpStatusCode.Forbidden);
        }
        await using (var final = new StoreManagementDbContext(options))
        {
            Assert.Equal(1, await final.Stores.CountAsync()); Assert.Equal(1, await final.Areas.CountAsync());
            Assert.Equal(1, await final.Equipment.CountAsync()); Assert.Equal(1, await final.DeviceBindings.CountAsync());
            Assert.Equal(2, await final.Measurements.CountAsync());
            Assert.Equal(1, await final.Alerts.CountAsync());
            var repository = new EquipmentTelemetryRepository(final);
            var binding = (await final.DeviceBindings.AsNoTracking().SingleAsync());
            var rows = await repository.Recent(binding.Id, default);
            Assert.Equal("m33-anomaly", rows[0].ReadingId);
            Assert.Equal("ShortCircuitRisk", rows[0].AnomalyType);
            Assert.Equal(220, rows.Single(r => r.ReadingId == "m33-normal").Voltage);
            Assert.Equal(220, binding.Thresholds.NominalVoltage);
        }
        Assert.Empty(data.Bindings); Assert.Empty(data.Measurements); // No memory persistence was used.
        Console.WriteLine("PASS new HTTP server and independent non-pooled EF sessions retain 1 store/area/equipment/binding and 2 measurements; missing machine scope denied; no legacy/relay calls");
    }

    private static async Task<JsonElement> Json(IDemoHttpScenario h, string path)
    {
        using var response = await h.Send(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    internal static async Task<JsonElement> ExerciseAlerts(IDemoHttpScenario h, string equipment, DbContextOptions<StoreManagementDbContext> options)
    {
        var page = await Json(h, equipment + "/alerts?status=OPEN&severity=CRITICAL&pageSize=1");
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var alert = page.GetProperty("items")[0];
        Assert.Equal("OPEN", alert.GetProperty("status").GetString());
        Assert.Equal("m33-anomaly", alert.GetProperty("readingId").GetString());
        Assert.Equal("Critical", alert.GetProperty("detectionSeverity").GetString());
        var url = equipment + "/alerts/" + alert.GetProperty("id").GetString();
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            var path = url + (method == HttpMethod.Post ? "/acknowledgement" : "");
            await Status(h.Send(method, path, otherUser: true), HttpStatusCode.NotFound);
            await Status(h.Send(method, path, anonymous: true, edgeKey: true), HttpStatusCode.Unauthorized);
            var wrong = path.Replace(path.Split('/')[6], "area-00000000000000000000000000000001", StringComparison.Ordinal);
            await Status(h.Send(method, wrong), HttpStatusCode.NotFound);
        }
        await using (var repeat = new StoreManagementDbContext(options))
        {
            var r = await repeat.Measurements.AsNoTracking().SingleAsync(r => r.ReadingId == "m33-anomaly");
            var b = await repeat.DeviceBindings.AsNoTracking().SingleAsync();
            var materializer = new AlertMaterializer(new AlertRepository(repeat));
            var source = new DetectedMeasurement(b.Id, r.ReadingId, b.EquipmentId, b.OwnerUserId, r.AnomalyType!, r.Severity!, r.CapturedAtUtc, r.ReceivedAtUtc);
            await materializer.Stage(source, default); await materializer.Stage(source, default);
            await repeat.SaveChangesAsync(); Assert.Equal(1, await repeat.Alerts.CountAsync());
        }
        await Status(h.Send(HttpMethod.Post, url + "/acknowledgement"), HttpStatusCode.OK);
        var first = await Json(h, url);
        Assert.Equal("ACKNOWLEDGED", first.GetProperty("status").GetString());
        Assert.Equal(h.Owner.Id.Value, first.GetProperty("acknowledgedByUserId").GetString());
        await Status(h.Send(HttpMethod.Post, url + "/acknowledgement"), HttpStatusCode.OK);
        var second = await Json(h, url);
        Assert.Equal(first.GetProperty("acknowledgedAtUtc").GetString(), second.GetProperty("acknowledgedAtUtc").GetString());
        Assert.Equal("ANOMALY", (await State(h, equipment)).GetProperty("state").GetString());
        // Force a later alert FK failure: the same SaveChanges must roll back the new measurement too.
        await using (var transaction = new StoreManagementDbContext(options))
        {
            var b = await transaction.DeviceBindings.AsNoTracking().SingleAsync();
            var time = DateTime.UtcNow.AddSeconds(-1);
            var reading = EquipmentMeasurement.Capture(b, "m34-rollback", time, 220, 30, 0.5f, 60, "EDGE", DateTime.UtcNow);
            transaction.Measurements.Add(reading);
            transaction.Alerts.Add(Alert.Open(new(b.Id, "missing-source", b.EquipmentId, b.OwnerUserId, "ShortCircuitRisk", "Critical", time, DateTime.UtcNow)));
            await Assert.ThrowsAsync<DbUpdateException>(() => new EquipmentTelemetryRepository(transaction).Save(default));
        }
        await using (var check = new StoreManagementDbContext(options))
        {
            Assert.False(await check.Measurements.AnyAsync(r => r.ReadingId == "m34-rollback"));
            Assert.Equal(1, await check.Alerts.CountAsync());
        }
        Console.WriteLine("PASS M3.4 OPEN alert, source uniqueness, human scoped access, first ACK audit, independent telemetry; measurement+alert failure rolls back together");
        return first;
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
    internal static async Task CheckSchema(NpgsqlConnection connection, StoreManagementDbContext context)
    {
        var tables = context.Model.GetRelationalModel().Tables.ToArray();
        Assert.Equal(6, tables.Length);
        Assert.Equal(5L, await Scalar(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema='store_management'"));
        Assert.Equal(1L, await Scalar(connection, "SELECT count(*) FROM information_schema.tables WHERE table_schema='alert_management'"));
        foreach (var table in tables)
        {
            await using var columns = new NpgsqlCommand("""
                SELECT a.attname, format_type(a.atttypid,a.atttypmod), NOT a.attnotnull
                FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname=@schema AND c.relname=@table AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attname
                """, connection);
            columns.Parameters.AddWithValue("schema", table.Schema!);
            columns.Parameters.AddWithValue("table", table.Name);
            var actual = new List<(string Name, string Type, bool Nullable)>();
            await using (var reader = await columns.ExecuteReaderAsync())
                while (await reader.ReadAsync()) actual.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
            Assert.Equal(table.Columns.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => (c.Name, c.StoreType, c.IsNullable)), actual.OrderBy(c => c.Name, StringComparer.Ordinal));
            var relation = table.Schema + ".\"" + table.Name + "\"";
            await using var constraints = new NpgsqlCommand("SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid=@relation::regclass AND contype IN ('p','f') ORDER BY conname", connection);
            constraints.Parameters.AddWithValue("relation", relation);
            var definitions = new Dictionary<string, string>();
            await using (var reader = await constraints.ExecuteReaderAsync())
                while (await reader.ReadAsync()) definitions.Add(reader.GetString(0), reader.GetString(1));
            static string Names(IEnumerable<string> names) => string.Join(", ", names.Select(n => "\"" + n + "\""));
            Assert.Equal(1 + table.ForeignKeyConstraints.Count(), definitions.Count);
            Assert.Equal("PRIMARY KEY (" + Names(table.PrimaryKey!.Columns.Select(c => c.Name)) + ")", definitions[table.PrimaryKey.Name]);
            foreach (var fk in table.ForeignKeyConstraints)
                Assert.Equal("FOREIGN KEY (" + Names(fk.Columns.Select(c => c.Name)) + ") REFERENCES " + fk.PrincipalTable.Schema + "." + fk.PrincipalTable.Name
                    + "(" + Names(fk.PrincipalColumns.Select(c => c.Name)) + ") ON DELETE RESTRICT", definitions[fk.Name]);
            await using var indexes = new NpgsqlCommand("""
                SELECT ci.relname, i.indisunique, array(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY k(num,ord)
                  JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.num ORDER BY k.ord)
                FROM pg_index i JOIN pg_class ci ON ci.oid=i.indexrelid WHERE i.indrelid=@relation::regclass AND NOT i.indisprimary
                """, connection);
            indexes.Parameters.AddWithValue("relation", relation);
            var indexDefs = new Dictionary<string, (bool Unique, string[] Columns)>();
            await using (var reader = await indexes.ExecuteReaderAsync())
                while (await reader.ReadAsync()) indexDefs.Add(reader.GetString(0), (reader.GetBoolean(1), reader.GetFieldValue<string[]>(2)));
            Assert.Equal(table.Indexes.Count(), indexDefs.Count);
            foreach (var index in table.Indexes)
            {
                Assert.Equal(index.IsUnique, indexDefs[index.Name].Unique);
                Assert.Equal(index.Columns.Select(c => c.Name), indexDefs[index.Name].Columns);
            }
        }
    }
    private static async Task Status(Task<HttpResponseMessage> responseTask, HttpStatusCode expected)
    {
        using var response = await responseTask;
        Assert.Equal(expected, response.StatusCode);
    }
    private static async Task<JsonElement> State(IDemoHttpScenario h, string path)
    {
        using var response = await h.Send(HttpMethod.Get, path + "/telemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Task<HttpResponseMessage> Ingest(IDemoHttpScenario h, Payload body) =>
        h.Send(HttpMethod.Post, "/api/v1/iot/edge/readings", body, anonymous: true, edgeKey: true);
    private sealed record Payload(string DeviceId, string ReadingId, DateTime Timestamp, float Voltage, float Current, float PowerFactor, float Frequency, string Source);
    private sealed record Manifest(string TaskId, string Database, string Host, int Port, string Password, string JwtSecret, string EdgeKey);
}
