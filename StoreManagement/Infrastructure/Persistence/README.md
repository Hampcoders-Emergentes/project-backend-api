# Persistencia M3.1 — preparación aditiva

Actualización M3.3: el recorrido con repositorios EF reales y PostgreSQL local ya fue ejecutado; ver [tests/M3.3.md](../../../tests/M3.3.md) desde la raíz del repositorio y `tools/StoreManagementPostgresDemo/`. Los párrafos M3.1/M3.2 siguientes conservan sus límites históricos. Los DDL se ensayaron únicamente en bases nuevas descartables; siguen sin ser un baseline para bases existentes.

StoreManagementDbContext usa el esquema store_management, con tablas stores, areas y equipment, dos FKs obligatorias y DeleteBehavior.Restrict. Store.OwnerUserId es UserId de IAM mediante conversión a texto, obligatorio e indexado con Id. La relación con IAM es un contrato de identidad, sin FK ni conversión a perfiles/propietarios heredados. No se cambian tablas de Assets/Profiles/IAM.

El módulo está registrado en DI con la misma conexión DefaultConnection existente, repositorio EF y servicio de aplicación. **No ejecuta EnsureCreated o Migrate.** El EnsureCreated heredado de Program no se amplió ni se ejecutó en esta tarea; tampoco crea/actualiza este contexto nuevo. Para usar los endpoints sobre una base existente hará falta una preparación de esquema autorizada después de conciliar su baseline.

Sql/001-store-hierarchy.draft.sql se regeneró offline con titularidad aprobada, desde el mismo modelo EF. Se revisó el modelo, las FKs y la traducción Npgsql de consultas con titularidad/ascendencia mediante ToQueryString. Ninguna de esas comprobaciones abre conexiones. **No se aplicó DDL ni se probó PostgreSQL real:** Docker CLI no pudo conectar a su daemon.

## Ensayo exclusivo en base local nueva y descartable

Acción técnica pendiente: iniciar Docker Desktop manualmente, comprobar docker info y seleccionar una imagen PostgreSQL local ya autorizada mediante docker image ls. No se descargan imágenes automáticamente. Con M31_POSTGRES_IMAGE_ID definido por el operador como ese ID:

```bash
docker run --pull=never --name electrolink-m31-disposable \
  --label electrolink.m31.disposable=true \
  -p 127.0.0.1:55431:5432 \
  -e POSTGRES_HOST_AUTH_METHOD=trust \
  -e POSTGRES_DB=electrolink_m31_synthetic \
  -d "${M31_POSTGRES_IMAGE_ID:?Selecciona primero una imagen PostgreSQL local autorizada}"
docker exec electrolink-m31-disposable pg_isready -U postgres -d electrolink_m31_synthetic
```

Solo cuando pg_isready confirme disponibilidad, desde la raíz del backend, preparar **esa base nueva**:

```bash
docker exec -i electrolink-m31-disposable \
  psql -U postgres -d electrolink_m31_synthetic -v ON_ERROR_STOP=1 \
  < StoreManagement/Infrastructure/Persistence/Sql/001-store-hierarchy.draft.sql
```

Estos comandos son instrucciones pendientes, no ejecuciones realizadas. No publicar el puerto en otra interfaz, reutilizar nombre/base compartida o aplicar el script repetidamente sobre tablas existentes. El modo trust es únicamente para este contenedor sintético enlazado a loopback. El SQL no lleva IF NOT EXISTS para ocultar tablas incompatibles: debe fallar si ya hay tablas con esos nombres.

Después hay que ejecutar el mismo recorrido de altas/consultas con StoreHierarchyRepository real sobre Host=127.0.0.1;Port=55431;Database=electrolink_m31_synthetic;Username=postgres, en una composición TestServer aislada con usuarios sintéticos y sin Program/jobs. El TestServer actual sustituye persistencia; aún no se realizó ese ensayo con repositorio real. No iniciar Program contra una base que solo tenga estas tablas: la presencia de tablas de otro contexto puede impedir que el EnsureCreated heredado cree sus tablas. Probar FK/rollback/errores reales pertenece a ese ensayo local, no se acredita por tener DDL.

## Base existente y recuperación

Este script no es una migración inicial para el esquema heredado. Antes de cualquier evolución sobre una base existente: copia autorizada, baseline real, verificación de colisiones/permisos, cambio aditivo revisado y recuperación ensayada. No asumir que EnsureCreated actualizará tablas. Si alguien aplicó una versión anterior del borrador, no rellenar OwnerUserId por inferencia; requiere conciliación explícita.

En esta tarea no hay esquema o datos reales que revertir. Para retirar código, quitar solo el registro nuevo de Program y los archivos del módulo/documentación/pruebas de esta entrega, revisando cambios posteriores y conservando la corrección demo y todo el legado. Si el esquema se aplica en una tarea futura, no borrar filas o usar DROP SCHEMA CASCADE a ciegas; identificar referencias y conservar datos.


## Adición M3.2 (sin aplicación)

El mismo contexto ahora incorpora device_bindings y measurements. El snapshot de umbrales vive en columnas del vínculo, no como tabla adicional. Tres índices únicos impiden duplicar EquipmentId, DeviceId o EdgeDeviceId; PK (BindingId, ReadingId) evita dos filas lógicas de la misma lectura. Hay FKs Restrict equipo→vínculo→medición y un índice (BindingId,CapturedAtUtc). Las referencias de propiedad/perfil son evidencia del puente ACL, no FKs ni conversiones de tablas heredadas.

En una base NUEVA y descartable, después del 001 revisado, revisar/aplicar exclusivamente allí Sql/002-equipment-telemetry.draft.sql usando el mismo psql con ON_ERROR_STOP=1 de las instrucciones anteriores. El 001 se conserva como artefacto M3.1; no se regeneró incluyendo todas las tablas. El 002 se generó offline seleccionando únicamente las dos tablas e índices nuevos del modelo completo. No ejecutar ninguno sobre una base compartida/existente ni asumir que son una migración validada del legado.

El ensayo real PostgreSQL M3.2 sigue pendiente: Docker no pudo conectar al daemon. El cuerpo de detección se verificó sin cambios; la validación HTTP usa dobles de persistencia/ACL y la EF usa construcción del modelo/DDL, no durabilidad SQL. No hubo creación, consulta o modificación de bases reales. La recuperación de un esquema futuro aplicado requiere conciliación y respaldo; no se retirarán vínculos/mediciones reales con un DROP automático.

## Repetir el ensayo M3.3

Seguir `tests/M3.3.md`: el auxiliar crea un contenedor y clúster exclusivos con imagen local, publica solo loopback, verifica propiedad/base vacía y aplica 001+002 en una transacción. El ejecutor HTTP conserva ambos repositorios EF y comprueba el catálogo frente al modelo. No ejecuta Program, EnsureCreated ni Migrate. El comando stop valida la propiedad y conserva los datos; no borra contenedores ni volúmenes. No reutilizar las bases históricas del manual M3.1 ni la base ya poblada de una demostración.

## M3.4 — alerta en la misma transacción

El contexto añade el mapeo `AlertManagement/Infrastructure/AlertMapping.cs` para `alert_management.alerts`: 6 tablas en total (5 StoreManagement + 1 AlertManagement). Se preserva la separación del código de dominio, usando el contexto compartido como puente transaccional explícito. Una única llamada SaveChanges de ingesta guarda medición y alerta; el materializador no guarda por separado. ACK se ejecuta mediante SQL condicional inmediato y no cambia mediciones.

DDL 003-alert-management.draft.sql aditivo, generado desde el modelo, ejecutado después de 001/002 únicamente en una base NUEVA de M3.4. El auxiliar de M3.3 ahora prepara los tres DDL y el ejecutor verifica los dos esquemas. No reutilizar las bases pobladas de M3.3 ni aplicar 003 a otra base: el ensayo no autoriza evolución de inventario existente. Evidencia, contratos y recuperación en tests/M3.4.md y AlertManagement/README.md desde la raíz.

## M3.5 — autoridad real de IAM/Profiles/Assets

El modo optativo `--real-backend` del ejecutor ya se comprobó con PostgreSQL, repositorios y servicios reales también para usuarios, perfiles, portafolio, propiedad y dispositivo. Se exporta offline el DDL completo de AppDbContext, se revisa y se solicita explícitamente con `prepare --legacy-schema`; solo se aplica en una base NUEVA vacía, antes de 001/002/003. No se ejecutan Program/EnsureCreated/Migrate. PostGIS procede de la imagen local ya disponible.

Corrección acotada de integración: constructor de AppDbContext tipado `DbContextOptions<AppDbContext>` para evitar recibir las opciones de StoreManagementDbContext cuando ambos se registran en DI. Sin cambio de modelo/proveedor. Preparación técnica de inventario mediante servicios, no instalación HTTP/física; handlers y centinelas acotados. Ver `tools/StoreManagementPostgresDemo/README.md` y `tests/M3.5.md` desde la raíz para comandos y límites.
