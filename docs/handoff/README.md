# ElectroLink Backend - Handoff

## Objetivo

Continuar la migración gradual del backend ElectroLink desde OpenCode hacia Codex, conservando el código, las decisiones, la evidencia acumulada y documentando el traspaso correctamente.

## Estado Actual

- **Repositorio:** `/Volumes/DISCO EXTER/Ciclo 8 - 202620/Emergentes/backend-api/electrolink-backend-api`
- **Rama:** `main` (tracking `origin/main`)
- **HEAD:** `8b62b619de1d998a06f198bd28bda1e118ebba5f`
- **Fecha:** 27 de octubre de 2026
- **Arquitectura:** Monolito modular ASP.NET Core / .NET 9
- **Stack:** EF Core 9.0.5, Npgsql 9.0.4, PostgreSQL con PostGIS
- **Patrón:** DDD con Bounded Contexts, CQRS (MediatR), Repository Pattern, Unit of Work

### Validación más reciente (M3.13)
- **Pruebas:** 218/218 aprobadas, 0 fallidas, 0 omitidas; se conservan las 210 anteriores
- **Build API:** 0 errores con exclusión temporal AppleDouble por CLI (NU1603 previo); no es una garantía del build ordinario
- **Último corte completado:** M3.13 - Instalación lógica HTTP por técnico asignado
- **Sin commits/push/despliegues:** Cambios sin commit preservados

## Instrucciones Vigentes

- **AGENTS.md (raíz):** Verificación en runtime, nomenclatura JWT (sub = user ID us-..., no profile ID). Rutas reales de endpoints de perfiles se encuentran en `Profiles/Interfaces/REST/ProfilesController.cs` (p.ej. `/api/v1/profiles/me/complete/homeowner`), no rutas antiguas heredadas sin contraste.
- **Prohibido:** commits, push, despliegues, reset/clean, restauraciones globales
- **Prohibido:** usar bases compartidas/productivas/desconocidas, arrancar Program con EnsureCreated/Migrate en entorno habitual
- **Prohibido:** activar workers/jobs/outbox/MQTT/proveedores externos/hardware
- **Solo:** ensayos locales nuevos, propios y verificados
- **Preservar:** todos los cambios sin commit, archivos nuevos, metadatos de Rider

## Orden de Lectura

1. **Este README** - Punto de entrada
2. **HISTORY.md** - Cronología completa de cortes M0-M3.9
3. **CODEMAP.md** - Mapa actual del código
4. **BACKLOG.md** - Pendientes ordenados
5. **RETURN-TO-CODEX.md** - Resumen para retomar
6. **Fuentes:** context.md, migration.md, AGENTS.md, informes M*.md en tests/

## Entorno

- **SDK:** .NET 9 disponible en /private/tmp/electrolink-dotnet9 (según M3.9)
- **BD:** PostgreSQL con soporte PostGIS
- **Contextos DB:** AppDbContext (módulos heredados) + StoreManagementDbContext (nuevas capacidades, puente con AlertManagement)

## Comandos Reales

```bash
# Build
dotnet build Hampcoders.Electrolink.API.csproj --no-restore

# Test (desde raíz)
dotnet test tests/Electrolink.Backend.Characterization.Tests --no-restore

# Test focalizado M3.9
dotnet test tests/Electrolink.Backend.Characterization.Tests --no-restore --filter 'FullyQualifiedName~MeasurementHistoryHttpTests|FullyQualifiedName~EquipmentTelemetryHttpTests'
```

**Nota:** No ejecutar `dotnet run` en entorno habitual sin verificar. Los ensayos PostgreSQL usan destinos locales nuevos.


## Continuidad posterior — M3.10

[Company HTTP real](../../tests/M3.10.md): recorrido funcional verificado en PostgreSQL nuevo (complete/company 201, perfil/sujeto Active, GET titular 200/ajeno 404). Suite xUnit 204/204, separada del modo optativo --company-http ejecutado. El 400 histórico no se reprodujo y su causa permanece pendiente de evidencia original. La autorización directa del usuario para este corte sustituye la antigua limitación a documentación; no habilita otra funcionalidad. Ver la actualización final de RETURN-TO-CODEX.md.


Company por capas también verificado: [M3.10-layers](../../tests/M3.10-layers.md). Incluye validación JSON/dominio/SQL/unicidad/eventos/DTO. El TRX histórico apunta a la etapa sign-up; no contiene cuerpo para identificar el dato rechazado. No usar el filtro como si fuera una captura histórica.


## Resultado funcional M3.11

[Informe M3.11](../../tests/M3.11.md): from/to sobre CreatedAtUtc del GET existente de alertas, con permisos/DTO y consulta sin fechas conservados. PostgreSQL relevante pasó, recurso propio detenido y datos retenidos. Company M3.10 queda demostrado dentro de su alcance; 400 histórico: causa no recuperable con la evidencia disponible; reabrir solo si reaparece con captura por etapa. Las restricciones históricas que bloqueaban otra funcionalidad fueron sustituidas por el encargo actual.


## Continuidad vigente M3.12

Leer tests/M3.12.md. GET por local implementado, DTO y rutas anteriores conservados; 18/18 focalizadas y 210/210 suite. PostgreSQL --store-alert-history demostrado, destino nuevo propio detenido/conservado. Sin esquema nuevo, cliente real ejecutado ni offline. Para compilar usar la invocación entrecomillada AppleDouble del informe; %3B no excluyó auxiliares en esta ejecución. No borrar metadatos ajenos o asumir build ordinario limpio. No iniciar otra tarea por una propuesta del relevo.


## Continuidad vigente M3.13

Leer tests/M3.13.md y Assets/README.md. Instalación lógica HTTP cerrada dentro de política confirmada; 19/19 focalizadas y 218/218 suite. PostgreSQL --installation-http real pasado; los tres destinos propios del corte están detenidos con datos retenidos. Registro/asignación, matching automático, activaciones de otros contextos, hardware y clientes reales siguen pendientes. Sin DDL o commits; usar exclusión AppleDouble entrecomillada documentada y no borrar metadatos. No iniciar automáticamente la propuesta siguiente.


## Publicación del acumulado autorizada — 27 de octubre de 2026

El usuario autorizó expresamente crear el commit necesario y subir todos los cambios acumulados a `develop` de `Hampcoders-Emergentes/project-backend-api`. Esta autorización es específica de esta publicación y sustituye la restricción previa de no commit/push para este envío; no autoriza despliegues ni cambios sobre bases. Los informes M0–M3.13 conservan el estado histórico anterior a esta publicación (main @ 8b62b619, sin commit). Consultar `git log -1 develop` para el commit publicado.

Verificación previa al envío: build API y ejecutor con 0 errores usando exclusión AppleDouble documentada; suite 218/218, 0 fallidas/omitidas. Código, pruebas, herramientas e informes/relevo incluidos; metadatos IDE, auxiliares AppleDouble, binarios y manifiestos privados fuera del commit. No se ejecutó el host habitual ni ensayos sobre bases durante esta publicación.
