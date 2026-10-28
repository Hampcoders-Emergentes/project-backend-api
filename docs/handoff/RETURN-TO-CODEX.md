# RETURN TO CODEX

> Continuidad actual: M3.16, stock propio del técnico. Leer [tests/M3.16.md](../../tests/M3.16.md) y la última sección de este relevo; las políticas de stock global y la propuesta Admin anteriores quedaron sustituidas por la corrección del usuario.


## Punto de Partida

**Ubicación:** `/Volumes/DISCO EXTER/Ciclo 8 - 202620/Emergentes/backend-api/electrolink-backend-api`

**Estado Git:**
- Rama: `develop` tracking `origin/develop`
- HEAD: `42b9fb8d63292c4d5341af119e2b9a8db84d7948` - "feat: publish accumulated ElectroLink backend migration"
- **Cambios sin commit:** Múltiples archivos modificados + nuevos (ver git status)
- **Archivos nuevos sin tracking:** StoreManagement completo ampliado, AlertManagement/, varios en Assets/Processing/IAM

## Trabajo Hecho en OpenCode

**Último corte:** M3.9 - Historial autorizado de mediciones del equipo (COMPLETADO, SIN COMMIT)

**Logros:**
- Implementado `GET /api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/measurements`
- Autorización por titularidad + ascendencia, disponible tras retirada dispositivo
- Paginación con from/to UTC, intervalo [from,to), max 31 días
- Orden por CapturedAtUtc asc + ReadingId asc
- Respuesta {items,page,pageSize,hasMore}, sin total global
- 4 pruebas nuevas + todas anteriores pasan (204/204)
- Demostración PostgreSQL real exitosa
- Build limpio (0 errores API)

## Archivos Afectados (M3.9, según informe)

**Nuevos:**
- StoreManagement/Application/MeasurementHistoryQuery.cs
- tests/Electrolink.Backend.Characterization.Tests/StoreManagement/MeasurementHistoryHttpTests.cs
- tests/Electrolink.Backend.Characterization.Tests/StoreManagement/M39MeasurementHistoryDemo.cs

**Modificados:**
- StoreManagement/Application/EquipmentTelemetryService.cs
- StoreManagement/Domain/Repositories/IEquipmentTelemetryRepository.cs
- StoreManagement/Infrastructure/Persistence/EquipmentTelemetryRepository.cs
- StoreManagement/Interfaces/REST/EquipmentDeviceController.cs
- StoreManagement/README.md
- tests/Electrolink.Backend.Characterization.Tests/StoreManagement/EquipmentTelemetryScenario.cs
- tools/StoreManagementPostgresDemo/Program.cs
- tools/StoreManagementPostgresDemo/README.md

**Total:** 11 archivos de código/soporte (3 nuevos + 8 modificados); 12 incluyendo tests/M3.9.md (4 nuevos + 8 modificados).  
**Nota:** El checkout actual tiene 15 archivos modificados + 105 sin tracking en total (acumulado de cortes previos M0.1-M3.8). Distinguir trabajo acumulado vs trabajo específico de cada corte.

## Decisiones Nuevas (M3.9)

1. **Autorización sin ingesta:** Usa GetEquipment para verificar Store→Area→Equipment del titular, NO consulta MachineLink/AllowedDeviceIds
2. **Tras retirada:** Acceso mantenido por titularidad del store (conserva histórico)
3. **Filtrado por captura:** Usa CapturedAtUtc (no receivedAtUtc)
4. **Orden determinista:** CapturedAtUtc + ReadingId asc, collation C
5. **Paginación offset:** No garantiza snapshot (llegadas tardías desplazan)
6. **Sin totales:** hasMore basado en pageSize+1
7. **Campos exactos:** Solo valores almacenados, sin derivados

## Comandos/Resultados Reportados

```bash
# Tests focalizados
dotnet test tests/Electrolink.Backend.Characterization.Tests --no-restore --filter 'FullyQualifiedName~MeasurementHistoryHttpTests|...'
# Resultado: 15/15 OK

# Suite completa
dotnet test tests/Electrolink.Backend.Characterization.Tests --no-restore
# Resultado: 204/204, 0 fallidas, 0 omitidas

# Build
dotnet build Hampcoders.Electrolink.API.csproj --no-restore
# Resultado: 0 errores, 1 NU1603 preexistente
```

**Nota:** Estos son resultados reportados, NO ejecutados en esta sesión. VERIFICAR estado actual.

## Bloqueos

- **M2.5:** Falta exports sanitizados/autorizados
- **SDK:** Mencionado /private/tmp/electrolink-dotnet9 - verificar disponibilidad
- **Bases:** No usar compartidas/productivas. Ensayos con destinos locales nuevos
- **Sin commit:** Trabajo acumulado sin commit - NO commitear sin autorización explícita

## Alcance autorizado vigente

El encargo del 25 de octubre autoriza un corte funcional pequeño de migration.md. M3.11 implementa filtro temporal de alertas del equipo (US19), con consulta existente y políticas conservadas. Company queda demostrado dentro de M3.10. El 400 histórico tiene causa no recuperable con la evidencia disponible; reabrir solo si reaparece con captura por etapa. No bloquea continuar.

M3.9b/detalle ReadingId no se inicia: sigue necesitando un consumidor/requisito respaldado y descartar duplicación.

## Instrucciones para Codex

1. **Verificar:** git status, rama, commit, archivos sin commit
2. **Contrastar:** Leer AGENTS.md, context.md, migration.md, M3.9.md
3. **Validar:** Compilar y ejecutar tests si SDK disponible
4. **Preservar:** Todo trabajo sin commit, no reset/clean
5. **Documentar:** Cualquier discrepancia entre reportado y observado
6. **NO ejecutar:** Program, EnsureCreated/Migrate, workers, outbox
7. **Continuar SOLO:** Tarea explícitamente autorizada

## Mensaje para Retomar

```
Retoma ElectroLink desde docs/handoff/README.md y RETURN-TO-CODEX.md. 
Lee el último informe (tests/M3.9.md) y contrasta el checkout. 
Preserva el trabajo de OpenCode. Identifica el punto exacto de continuidad, 
los resultados reportados y los pendientes. No repitas trabajo terminado 
ni ejecutes operaciones prohibidas. Continúa únicamente la tarea explícitamente 
autorizada en el relevo; si no hay una, propón la siguiente.
```


## Actualización vigente — M3.10

La instrucción directa del usuario autorizó completar Company HTTP real y sustituyó la limitación anterior de “solo documentación”. No autoriza funcionalidades adicionales.

- Recorrido Company HTTP **verificado**: dos signup/complete-company/login reales; complete/company 201 application/json, perfil Active y sujeto Company activo desde otro scope, portafolio Assets creado por handler real. Propiedad/dispositivo preparados técnicamente por servicios; GET titular 200 con los ocho campos y ajeno 404, sin mutaciones/eventos por lectura.
- Regresión reproducible: `tools/StoreManagementPostgresDemo --company-http <manifiesto-privado-de-base-nueva>`. Es opt-in y **no forma parte de 204/204 xUnit**.
- Build API 0 errores (NU1603 previo), focalizadas 11/11, suite final 204/204. Logs y recuperación selectiva en tests/M3.10.md.
- **Causa del 400 anterior NO confirmada:** no se reprodujo con el helper actual. Sin corrección de producción ni cambio de enum/DNI/TaxId/validadores. Se solicitó evidencia sanitizada original. Diagnóstico pendiente, no presentarlo como resuelto.
- Dos bases propias detenidas y conservadas, sin usar bases anteriores/compartidas. Sin commits/push/despliegues.

Siguiente acción dentro de este encargo: analizar la petición/error original del 400 cuando esté disponible y reproducir esa condición aisladamente. No repetir el recorrido ya acreditado sin cambios que lo justifiquen. No iniciar M3.9b u otra funcionalidad.

Conteo M3.9: 11 archivos de código/soporte; 12 con tests/M3.9.md (4 nuevos + 8 modificados). [Informe nuevo](../../tests/M3.10.md).


### Actualización de continuidad — Company por capas

La evidencia histórica ahora incluye un TRX sin cuerpo, con frame RealBackendScenario.cs:139, línea de sign-up en la copia fuente guardada antes de instrumentar. La atribución original a complete/company no está acreditada. El usuario confirmó que no hay payload histórico guardado: DomainExceptionFilter genera las respuestas, no las archiva. Motivo exacto antiguo sin determinar; capturar por etapa si recurre, sin inventarlo.

--company-layers está ejecutado con PostgreSQL nuevo: model binding 400 antes de acción; teléfono inválido 400 de dominio/filtro; TaxId duplicado 409 real; 201 correcto. Veintidós campos persistidos, evento ProfileCompleted, DTO y GET de dispositivo titular/ajeno correctos. Las pruebas xUnit siguen 204/204 (focalizadas 11/11), modo PostgreSQL aparte.

La edición del usuario en DomainExceptionFilter (TaxId 409/InvalidTaxId 400) y su AppleDouble se preservaron. Build requiere la exclusión temporal CLI documentada mientras ese auxiliar exista; no se borró ni se cambió el proyecto. Tres contenedores propios de esta continuación detenidos y conservados. [Informe y recuperación selectiva](../../tests/M3.10-layers.md).


## Instrucción vigente posterior a M3.10

El encargo del 25 de octubre autoriza seleccionar e implementar un único corte funcional pendiente de migration.md. Sustituye las frases antiguas que impedían otra funcionalidad. Company está demostrado dentro de M3.10. 400 histórico: causa no recuperable con la evidencia disponible; reabrir si reaparece con captura por etapa. No bloquea migración.

Corte seleccionado M3.11: intervalo temporal del historial de alertas por equipo (US19), sin políticas nuevas de incidentes. Build ordinario puede fallar CS2015 por AppleDouble; el build con exclusión temporal CLI se reporta como tal. Filtro TaxId y Rider: trabajo del usuario preservado.


## Resultado M3.11

Implementado filtro from/to por CreatedAtUtc en el GET de alertas del equipo. UTC [from,to), máximo 31 días, misma página/DTO/filtros/propietario y ascendencia; sin fechas conserva la consulta anterior. Sin DDL, sin cambiar ACK/resolución ni generar avisos. Parser UTC extraído a Shared para reutilizarlo sin modificar el contrato de mediciones.

Verificados: RED 3 casos; focalizadas 9/9; suite 207/207 (204 anteriores preservadas); --alert-history PostgreSQL aparte, pasado y detenido conservando datos. Build 0 errores con exclusión temporal AppleDouble CLI, no afirmar build ordinario incondicional. Mapeos del filtro y Rider son trabajo del usuario preservado. [Informe](../../tests/M3.11.md).

No seguir buscando el cuerpo perdido del 400 ni ampliar Company sin defecto nuevo. M2.5 continúa bloqueado solo para conciliación real. No avanzar automáticamente a otra capacidad.


## Actualización vigente — M3.12 (27 de octubre de 2026)

Esta sección y el encargo directo M3.12 sustituyen la continuidad anterior M3.11. Se completó únicamente el historial de alertas por local con equipo opcional: GET /api/v1/stores/{storeId}/alerts. Reutiliza AlertResource (incluye EquipmentId), políticas IAM/jerarquía, fechas UTC CreatedAtUtc y filtros/página de M3.11. EF aplica alcance/filtros/orden/Skip/Take; no N+1 por equipos, DDL o autoridad de ingesta.

Build API 0 errores con exclusión CLI AppleDouble entrecomillada, NU1603 previo; focalizadas 18/18 y suite 210/210, cero fallidas/omitidas. --store-alert-history sobre PostgreSQL nuevo pasado; contenedor propio detenido con datos retenidos. Resultados de este corte en tests/M3.12.md. El argumento %3B previo no excluyó auxiliares presentes en esta ejecución; ver invocación corregida y comparación Compile items, sin quitar código/pruebas legítimos ni borrar metadatos. SDK oficial 9.0.319 recuperado aislado con la autorización previa, sin cambiar runtime objetivo/paquetes.

Los apartados M3.9–M3.11 anteriores son contexto histórico. Company se mantiene demostrado dentro de M3.10: causa del 400 histórico no recuperable con la evidencia disponible; reabrir si reaparece con captura por etapa. No bloquea. M2.5 solo bloquea conciliación real. Sin consumidores reales ejecutados, offline, resolución, notificaciones, hardware o producción acreditados. No comenzar otra tarea sin encargo autorizado. Propuesta pequeña pendiente en BACKLOG; recuperación selectiva en tests/M3.12.md, no restaurar globalmente ni tocar Rider.


## Actualización vigente — M3.13

Se completó exclusivamente PUT /api/v1/io-t-devices/{id}/install/{propertyId}. Política de escritura confirmada en este chat: perfil Technician activo del actor IAM, técnico del cuerpo coincidente y asignación persistida a solicitud Installation compatible con dispositivo Assigned y Property activa/no archivada. La autorización está en DeviceInstallationService; Planning expone lector ACL acotado sin jobs. Los permisos de lectura/baja no se extrapolan. Comandos técnicos internos, método/cuerpo/DTO y esquema se conservan.

Verificados build 0 errores con exclusión AppleDouble (NU1603 previo), 19/19 focalizadas y 218/218 suite (210 anteriores intactas +8). PostgreSQL opt-in --installation-http aprobado; un cliente y dos técnicos reales, precondiciones técnicas explícitas, éxito y rechazos sin efectos externos, scopes nuevos. Tres recursos propios detenidos y conservados, incluidos dos ensayos de fixture corregidos sin tocar producción. Informe/comandos/recuperación exclusiva en tests/M3.13.md.

Registro y asignación todavía son técnicos en el ensayo. El flujo comercial IoT PendingInstallation, selección automática de dispositivo, Processing/Subscriptions, carreras, hardware y clientes no se acreditan. M2.5 bloquea solo conciliación real. Company y 400 histórico mantienen estado de M3.10/M3.12. Continuar únicamente con nueva tarea autorizada; propuesta pendiente en BACKLOG. Preservar todo el acumulado, Rider y mapeos TaxId del usuario; sin reset/clean, commits o bases compartidas.


## Publicación del acumulado autorizada — 27 de octubre de 2026

El usuario autorizó expresamente crear el commit necesario y subir todos los cambios acumulados a `develop` de `Hampcoders-Emergentes/project-backend-api`. Esta autorización es específica de esta publicación y sustituye la restricción previa de no commit/push para este envío; no autoriza despliegues ni cambios sobre bases. Los informes M0–M3.13 conservan el estado histórico anterior a esta publicación (main @ 8b62b619, sin commit). Consultar `git log -1 develop` para el commit publicado.

Verificación previa al envío: build API y ejecutor con 0 errores usando exclusión AppleDouble documentada; suite 218/218, 0 fallidas/omitidas. Código, pruebas, herramientas e informes/relevo incluidos; metadatos IDE, auxiliares AppleDouble, binarios y manifiestos privados fuera del commit. No se ejecutó el host habitual ni ensayos sobre bases durante esta publicación.


## Actualización vigente — M3.14, punto 1 en progreso

El usuario pidió empezar paso a paso por aprovisionamiento y confirmó: solo técnico IAM/Profiles activo asignado a solicitud Installation válida, propiedad activa correspondiente y dispositivo libre del stock global elegido explícitamente; sin bypass Admin/Edge ni asignación con ID vacío. Esta política es independiente de lectura/baja/instalación.

Implementado POST assign protegido con DeviceAssignmentService y lector Planning reutilizado. Mismo método/cuerpo/DTO; 200 autorizado, 404 genérico por relación ausente, 401 anónimo/Edge, 409 stock ocupado/repetición secuencial. El puente interno exige SelectedDeviceId y deja de elegir el primer stock. El productor Planning todavía emite ID vacío y el flujo comercial queda pendiente, sin asociaciones inventadas.

Verificación actual: baseline 218/218; focalizadas 29/29; suite 228/228, cero fallidas/omitidas; PostgreSQL nuevo --assignment-http pasó asignación→instalación por HTTP, estado/relaciones desde scopes nuevos y rechazos sin efectos. Recurso propio detenido, datos conservados; no host habitual/jobs/proveedores. Build 0 errores con exclusión AppleDouble verificada, NU1603 previo. No nuevos commits/push; HEAD develop @42b9fb8 intacto. Informe y recuperación selectiva en tests/M3.14.md.

**Punto 1 no terminado:** la reserva aún no es atómica frente a concurrencia; es la siguiente corrección pequeña prioritaria. Restan alta HTTP con política propia y selección persistida en scheduling. La autorización previa de publicación correspondió solo al envío ya completado; no se repite automáticamente. Conservar Rider, cambios previos, metadatos y datos; M2.5 no bloquea desarrollo aislado.


## Continuidad vigente M3.15 — reserva del mismo dispositivo

Se reprodujo la carrera con dos POST autorizados leyendo InStock: 200/200, dos propiedades/eventos. Corregida con Status como token de concurrencia EF y 409 específico en assign; GREEN 200/409, una propiedad/evento, rollback de la perdedora. DDL offline idéntico; misma política, ruta y DTO. Focalizadas 29/29, suite 228/228, más una regresión PostgreSQL concurrente opt-in y compatibilidad asignación→instalación aprobadas. Tres destinos nuevos propios detenidos con datos retenidos. Build 0 errores con exclusión AppleDouble; NU1603 previo. Los 24 archivos previos con Fact/Theory están intactos. Sin stage/commit/push; develop @42b9fb8 y M3.14 preservados.

La pendiente de reserva en M3.14 queda atendida para competencia por **el mismo dispositivo**; punto 1 continúa abierto. Próximo incremento propuesto: definir política propia y proteger el registro HTTP de stock, sin heredar permisos de asignación/instalación. Después, selección persistida en workflow comercial; Planning sigue produciendo selección vacía y se deniega sin elegir stock implícito. También quedan carreras entre dispositivos distintos sobre una misma propiedad y frente a revocación/reasignación, no acreditadas aquí. Otros escritores EF de IoTDevice pueden ahora observar excepciones de concurrencia; no se cambia su manejo en este corte. No uso real, clientes o hardware acreditados. Ver tests/M3.15.md para archivos, comandos y recuperación exclusiva; M2.5 solo bloquea conciliación real.


## Continuidad vigente M3.16 — stock del técnico

Corrección expresa del usuario: el registro pertenece al técnico que gestiona su inventario; se descarta Admin/SuperAdmin como permiso de registro y se sustituye la selección de stock global de M3.14 por stock propio. IAM activo + Active Technician, subject concordante e inventario existente; actor del servidor, sin titular enviado por cuerpo. POST canonical /api/v1/iot-devices y alias anterior igualmente protegidos; GET my-stock paginado con cantidades total/disponible, GET propio por ID/Location, listados y contador HTTP legados aislados. Assign rechaza stock ajeno o sin titular y conserva relación de solicitud/propiedad y token concurrente.

Columna nueva nullable stock_technician_id e índice; sin atribuciones automáticas del stock previo. Borrador SQL 004 de Assets para revisión, **no aplicado a bases compartidas/existentes**. Destinos nuevos usan export actual cd69a8a040494b4aaa17f1c2eff4af9e922dd08d3438fd93588469384b66de6f. El código requiere esquema actualizado antes de un despliegue futuro autorizado; no se despliega aquí.

Verificación final 263/263 y 64/64 focalizadas, build 0 errores con exclusión AppleDouble. PostgreSQL real: registro propio→consulta→asignación→instalación, stock disponible 2→1, contadores activos 1/0, serial 201/409 y exclusión de asignación 200/409. Cinco destinos propios de la sesión detenidos/retienen datos; el primero fue un prototipo Admin descartado y no acredita la política vigente. Fixture M3.15 ahora usa dos solicitudes del mismo titular; DeviceAssignmentHttpTests actualiza stock válido a técnico explícito y agrega dos rechazos. No se debilitan las aserciones concurrentes; otras 23 fuentes previas Fact/Theory intactas.

El usuario autorizó commit/push de los cambios al terminar: publicación de M3.14/M3.15/M3.16 en develop de Hampcoders-Emergentes/project-backend-api, base anterior 42b9fb8. Consultar git log -1 develop y origen para el hash de la publicación; los informes anteriores conservan su estado histórico sin commit. Mantener metadatos Rider/AppleDouble, manifiestos y binarios fuera. No supone permiso de despliegue o cambios compartidos.

Punto 1 sigue pendiente de selección persistida y coordinación comercial en Planning/Subscriptions, transferencias y conciliación autorizada del stock sin titular. La consulta disponible permite revisar dispositivos, pero no valida automáticamente todos los materiales, cupos o requisitos del servicio ni reserva al consultar. No activar primer stock implícito para completar scheduling. M2.5 continúa pendiente solo para conciliación real. Pruebas, archivos y recuperación selectiva en tests/M3.16.md.
