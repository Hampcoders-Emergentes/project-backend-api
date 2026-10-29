# ElectroLink Backend - Handoff

> Continuidad actual: M3.16, stock propio del técnico. Leer [tests/M3.16.md](../../tests/M3.16.md) y la última sección de este relevo; las políticas de stock global y la propuesta Admin anteriores quedaron sustituidas por la corrección del usuario.


## Objetivo

Continuar la migración gradual del backend ElectroLink desde OpenCode hacia Codex, conservando el código, las decisiones, la evidencia acumulada y documentando el traspaso correctamente.

## Estado Actual

- **Repositorio:** `/Volumes/DISCO EXTER/Ciclo 8 - 202620/Emergentes/backend-api/electrolink-backend-api`
- **Rama:** `develop` (tracking `origin/develop`)
- **HEAD:** `42b9fb8d63292c4d5341af119e2b9a8db84d7948`
- **Fecha:** 27 de octubre de 2026
- **Arquitectura:** Monolito modular ASP.NET Core / .NET 9
- **Stack:** EF Core 9.0.5, Npgsql 9.0.4, PostgreSQL con PostGIS
- **Patrón:** DDD con Bounded Contexts, CQRS (MediatR), Repository Pattern, Unit of Work

### Validación más reciente (M3.14)
- **Pruebas:** 228/228 aprobadas, 0 fallidas, 0 omitidas; se conservan las 218 anteriores
- **Build API:** 0 errores con exclusión temporal AppleDouble por CLI (NU1603 previo); no es una garantía del build ordinario
- **Último corte completado:** M3.14 - Asignación explícita HTTP (primer incremento del punto 1)
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


## Continuidad vigente M3.14

develop conserva HEAD 42b9fb8 publicado; los cambios nuevos M3.14 están sin commit/push. El usuario autorizó iniciar paso a paso el punto 1 y confirmó por separado la política de asignación de stock global explícito por técnico activo/asignado. Ver tests/M3.14.md y Assets/README.md. 228/228 y 29/29 focalizadas; PostgreSQL --assignment-http pasado, detenido/conservado. Siguiente prioridad dentro del punto 1: reserva atómica concurrente; luego alta HTTP autorizada y selección persistida. No interpretar este incremento como aprovisionamiento completo ni repetir automáticamente la publicación anterior.


## Continuidad vigente M3.15 — reserva del mismo dispositivo

Se reprodujo la carrera con dos POST autorizados leyendo InStock: 200/200, dos propiedades/eventos. Corregida con Status como token de concurrencia EF y 409 específico en assign; GREEN 200/409, una propiedad/evento, rollback de la perdedora. DDL offline idéntico; misma política, ruta y DTO. Focalizadas 29/29, suite 228/228, más una regresión PostgreSQL concurrente opt-in y compatibilidad asignación→instalación aprobadas. Tres destinos nuevos propios detenidos con datos retenidos. Build 0 errores con exclusión AppleDouble; NU1603 previo. Los 24 archivos previos con Fact/Theory están intactos. Sin stage/commit/push; develop @42b9fb8 y M3.14 preservados.

La pendiente de reserva en M3.14 queda atendida para competencia por **el mismo dispositivo**; punto 1 continúa abierto. Próximo incremento propuesto: definir política propia y proteger el registro HTTP de stock, sin heredar permisos de asignación/instalación. Después, selección persistida en workflow comercial; Planning sigue produciendo selección vacía y se deniega sin elegir stock implícito. También quedan carreras entre dispositivos distintos sobre una misma propiedad y frente a revocación/reasignación, no acreditadas aquí. Otros escritores EF de IoTDevice pueden ahora observar excepciones de concurrencia; no se cambia su manejo en este corte. No uso real, clientes o hardware acreditados. Ver tests/M3.15.md para archivos, comandos y recuperación exclusiva; M2.5 solo bloquea conciliación real.


## Continuidad vigente M3.16 — stock del técnico

Corrección expresa del usuario: el registro pertenece al técnico que gestiona su inventario; se descarta Admin/SuperAdmin como permiso de registro y se sustituye la selección de stock global de M3.14 por stock propio. IAM activo + Active Technician, subject concordante e inventario existente; actor del servidor, sin titular enviado por cuerpo. POST canonical /api/v1/iot-devices y alias anterior igualmente protegidos; GET my-stock paginado con cantidades total/disponible, GET propio por ID/Location, listados y contador HTTP legados aislados. Assign rechaza stock ajeno o sin titular y conserva relación de solicitud/propiedad y token concurrente.

Columna nueva nullable stock_technician_id e índice; sin atribuciones automáticas del stock previo. Borrador SQL 004 de Assets para revisión, **no aplicado a bases compartidas/existentes**. Destinos nuevos usan export actual cd69a8a040494b4aaa17f1c2eff4af9e922dd08d3438fd93588469384b66de6f. El código requiere esquema actualizado antes de un despliegue futuro autorizado; no se despliega aquí.

Verificación final 263/263 y 64/64 focalizadas, build 0 errores con exclusión AppleDouble. PostgreSQL real: registro propio→consulta→asignación→instalación, stock disponible 2→1, contadores activos 1/0, serial 201/409 y exclusión de asignación 200/409. Cinco destinos propios de la sesión detenidos/retienen datos; el primero fue un prototipo Admin descartado y no acredita la política vigente. Fixture M3.15 ahora usa dos solicitudes del mismo titular; DeviceAssignmentHttpTests actualiza stock válido a técnico explícito y agrega dos rechazos. No se debilitan las aserciones concurrentes; otras 23 fuentes previas Fact/Theory intactas.

El usuario autorizó commit/push de los cambios al terminar: publicación de M3.14/M3.15/M3.16 en develop de Hampcoders-Emergentes/project-backend-api, base anterior 42b9fb8. Consultar git log -1 develop y origen para el hash de la publicación; los informes anteriores conservan su estado histórico sin commit. Mantener metadatos Rider/AppleDouble, manifiestos y binarios fuera. No supone permiso de despliegue o cambios compartidos.

Punto 1 sigue pendiente de selección persistida y coordinación comercial en Planning/Subscriptions, transferencias y conciliación autorizada del stock sin titular. La consulta disponible permite revisar dispositivos, pero no valida automáticamente todos los materiales, cupos o requisitos del servicio ni reserva al consultar. No activar primer stock implícito para completar scheduling. M2.5 continúa pendiente solo para conciliación real. Pruebas, archivos y recuperación selectiva en tests/M3.16.md.


## Continuidad vigente M3.17–M3.19 — aprovisionamiento por suscripción

- **M3.17** (commit local 29d0d13, sin push): la selección de dispositivo es solo por POST assign del técnico. Se retiraron el puente por evento con `SelectedDeviceId` y la fachada de primer stock global.
- **M3.18:** trazado y caracterización del ciclo Enterprise. Decisiones 1–4 aprobadas por el usuario; la decisión 0 (contrato del checkout: StripeSubscriptionId/InitialDeviceCount) sigue abierta.
- **M3.19** (sin commit):
  - `POST /api/v1/service-requests/{requestId}/installation-property`: el Company titular fija una propiedad propia, activa y no archivada; la solicitud pasa de PendingInstallation a PendingAssignment, se guarda y se ejecuta el matching existente sin cupo.
  - La fábrica exige el Client Company resuelto por Profiles: sin él, PostgreSQL rechazaba la fila (23502).
  - `ServiceRequest.Status` es token de concurrencia EF, sin DDL; export `cd69a8a0…` sin cambios.
  - 302/302 pruebas. PostgreSQL real: `--installation-property-http` y `--assignment-http` en verde, en destinos nuevos detenidos con sus datos.
- **Hallazgos abiertos:**
  - `GetPropertyByIdQuery` ignora el titular, así que el `SelectProperty` existente no verifica titularidad.
  - `ServiceAssignment` no se puede materializar desde sus jsonb.
  - Pendientes: activación al completar N, vencimiento parcial y Stripe real.
- **Detalle:** tests/M3.17.md, tests/M3.18.md y tests/M3.19.md.

### M3.20 — titularidad en `SelectProperty` (sin commit)

- `AssetsContextFacade.GetPropertyGeolocationAsync` y `PropertyBelongsToOwnerAsync` comparan ahora el titular, porque `GetPropertyByIdQuery` lo ignora.
- `SelectProperty` responde 404 genérico ante una propiedad ajena o inexistente (antes: 204 y 500).
- 310/310 pruebas.
- Abierto: `PropertiesController` toma el owner de la ruta sin vincularlo al actor (posible IDOR); propiedades inactivas o archivadas en `SelectProperty` sin política. Ver tests/M3.20.md.

### M3.21 — caracterización del IDOR en `PropertiesController` (sin corregir, sin commit)

- 7 pruebas de caracterización (317/317) confirman que cualquier JWT válido, incluso de un usuario suspendido, lista, lee, crea, desactiva, activa, mueve y archiva propiedades de otros owners, y obtiene URLs de foto firmadas nombrando a la víctima en la ruta.
- El middleware IAM no comprueba `User.Status`.
- Propuesta M3.22 y patrón similar en otros 5 controllers: ver tests/M3.21.md.

### M3.22 — `PropertiesController` vinculado al actor (sin commit)

- El `ownerId` de la ruta debe ser el subject HomeOwner o Company del actor IAM activo (`PropertyOwnerResolver`).
- Actor no elegible → 403; owner ajeno, prefijo cruzado o propiedad ajena o inexistente → 404 genérico.
- `GetPropertyByIdQuery` filtra por titular. Sin bypass Admin. Sin esquema.
- 333/333 pruebas. PostgreSQL `--property-ownership-http` y `--installation-property-http` en verde.
- Pendiente:
  - el mismo patrón en otros 5 controllers;
  - el middleware IAM sigue sin comprobar `User.Status`;
  - compatibilidad de los clientes web y móvil sin verificar.
- Ver tests/M3.22.md.

### M3.23 — `PropertyPortfoliosController` vinculado al actor (sin commit)

- Helper compartido `PropertyOwnerRoute`, también usado por `PropertiesController`.
- Owner ajeno o prefijo cruzado → 404; actor no elegible → 403.
- Una propiedad ajena al añadirla a un portfolio pasa de 403 (con el owner en el mensaje) a 404 genérico.
- 351/351 pruebas. PostgreSQL `--portfolio-ownership-http` y `--property-ownership-http` en verde.
- Incidencias de entorno:
  - el scratchpad se vació (SDK reinstalado);
  - Docker perdió todos los contenedores y volúmenes de los ensayos anteriores sin acción de la sesión.
- Pendiente: los controllers por `{technicianId}`. Ver tests/M3.23.md.

### M3.24 — `TechnicianInventoriesController` vinculado al técnico (sin commit)

- `TechnicianActorResolver` y `TechnicianRoute`: técnico IAM activo y concordante; ruta ajena → 404; no elegible → 403.
- Errores de dominio que antes daban 500 pasan a 404/409.
- 372/372 pruebas. PostgreSQL `--inventory-ownership-http` en verde.
- Pendiente: `ComponentsController` y `ComponentTypesController`, y decidir la pertenencia de componentes y tipos. Ver tests/M3.24.md.

### M3.25 — catálogo de componentes y tipos (sin commit)

- Decisión del usuario: escritura solo para Admin/SuperAdmin activos; lectura para técnicos activos en su ruta y para esos administradores (`ComponentCatalogAccess`).
- Duplicado o tipo en uso → 409; inexistente → 404 (antes 500).
- 395/395 pruebas. PostgreSQL `--component-catalog-http` en verde, incluida la mutación real de stock pendiente de M3.24.
- Pendiente: `ServiceCatalogController`, y comprobar la relación componente↔tipo al añadir stock. Ver tests/M3.25.md.

### M3.26 — `ServiceCatalogController` vinculado al técnico (sin commit)

- Decisión del usuario: catálogo y recetas solo para el propio técnico.
- El actor se resuelve en el servidor (`ExternalProfilesService.GetActiveTechnicianByUserIdAsync`), sin claims del JWT; `me` funciona en todas las acciones.
- 408/408 pruebas. PostgreSQL `--service-catalog-http` en verde.
- Pendiente: el middleware IAM no comprueba `User.Status` de forma global. Ver tests/M3.26.md.

### M3.27 — el middleware IAM rechaza cuentas no activas (sin commit)

- `RequestAuthorizationMiddleware` devuelve 403 `Forbidden: Account is not active` a una cuenta Suspended o PendingVerification con token válido, antes de cualquier endpoint humano.
- Sin cambios para los endpoints anónimos ni para el canal Edge.
- 418/418 pruebas. PostgreSQL `--registration-http` (con suspensión real) y `--installation-property-http` en verde.
- Pendiente:
  - el inicio de sesión sigue emitiendo tokens a cuentas suspendidas;
  - no hay endpoint para suspender o reactivar.
- Ver tests/M3.27.md.

### M3.28 — el inicio de sesión no emite tokens a cuentas no activas (sin commit)

- Con credenciales válidas, una cuenta Suspended o PendingVerification recibe 403 `Account is not active`, sin token ni evento; con una contraseña incorrecta sigue el 401 genérico.
- `refresh-claims` también da 403.
- 424/424 pruebas. PostgreSQL `--inactive-sign-in-http` y `--registration-http` en verde.
- Ver tests/M3.28.md.

### M3.29 — `ServiceRequestController` sin claims del JWT (sin commit)

- El cliente se resuelve con IAM y Profiles (HomeOwner o Company activo y concordante). Sin perfil elegible → 403; solicitud ajena, inexistente o mal formada → 404 (antes 500).
- `available-services` exige ser el titular.
- Un token con el perfil suspendido después de emitirse recibe 403 (PostgreSQL).
- 434/434 pruebas.
- Pendiente: `ServiceSuggestionController`, `SubscriptionsController` y Monitoring siguen usando claims. Ver tests/M3.29.md.

### Estado Git (verificado al cerrar M3.29)

- Una operación externa a la sesión (reflog, 2026-10-29 15:54:42) reescribió M3.17 como `4cb6d50` y publicó `f31bf28` (M3.18–M3.22) en `origin/develop`.
- M3.23–M3.29 siguen sin commit encima de `f31bf28`; las bases «29d0d13» de los informes M3.23–M3.28 deben leerse como `f31bf28`.
- Detalle en tests/M3.29.md.

### M3.30 — `ServiceSuggestionController` sin claims del JWT (sin commit)

- Cliente resuelto con IAM y Profiles. Sugerencia ajena o inexistente → 404 genérico (antes 403 o 500); conflicto de estado → 409.
- 447/447 pruebas. PostgreSQL `--service-suggestion-http` en verde.
- Anotado: aceptar una sugerencia crea la solicitud sin elegibilidad ni cupo.
- Pendiente: `SubscriptionsController` y Monitoring. Ver tests/M3.30.md.

### M3.31 — `SubscriptionsController` sin claims del JWT (sin commit)

- Decisión del usuario: perfiles Active y Suspended (pago fallido) pueden usar la suscripción; el resto → 403. El `ProfileId` se resuelve con IAM y Profiles.
- Corregido un defecto: `me/cancel` y `me/portal` daban 500 siempre, por `[property: Required]` en *records*.
- 456/456 pruebas. PostgreSQL `--subscription-profile-http` en verde.
- Pendiente: Monitoring. Ver tests/M3.31.md.
