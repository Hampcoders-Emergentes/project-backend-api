# Historia de Cortes - ElectroLink Backend

## Cronología

| Corte | Fecha/Estado | Descripción | Informe |
|---|---|---|---|
| M0 | Diagnóstico | Diagnóstico y caracterización inicial | (histórico) |
| M2.1-M2.3 | Completado | Separación autenticación humana/Edge, alcance explícito relay/history | tests/M2*.md (si existe) |
| M2.4 | Completado | Herramienta local conciliación IDs, sin concesión acceso | tests/M2.4.md (si existe) |
| M2.5 | **PENDIENTE** | Conciliación real - FALTA exports sanitizados y autorizados | - |
| M2.6-M2.8 | Completado | Caracterización outbox/eventos, corrección cancelación, delimitación defectos heredados | tests/M2*.md (si existe) |
| M3.1 | Completado | Locales, áreas, equipos con 9 endpoints humanos; corrección pertenencia handler dispositivo demo | tests/M3.1.md (si existe) |
| M3.2 | Completado | Vínculo autorizado dispositivo, ingesta vinculada, telemetría | tests/M3.2.md (si existe) |
| M3.3 | Completado | Persistencia PostgreSQL real, normalización temporal microsegundos | tests/M3.3.md (si existe) |
| M3.4 | Completado | Alertas OPEN/ACKNOWLEDGED, reconocimiento humano, atomicidad medición/alerta | tests/M3.4.md (si existe) |
| M3.5 | Completado | Recorrido con IAM/Profiles/Assets reales, corrección opciones tipadas AppDbContext | tests/M3.5.md (si existe) |
| M3.6 | Completado | Retirada lógica invalida ingesta, conserva histórico | tests/M3.6.md (si existe) |
| M3.7 | Completado | Corrección autorización HTTP baja | tests/M3.7.md (si existe) |
| M3.8 | Completado | Corrección autorización consulta individual dispositivo | tests/M3.8.md (si existe) |
| M3.9 | Completado (sin commit) | **Historial autorizado mediciones por equipo** - GET /api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/measurements | [tests/M3.9.md](../../tests/M3.9.md) |

## Cambios Principales M3.9

**Nuevos archivos (del corte M3.9):**
- `StoreManagement/Application/MeasurementHistoryQuery.cs`
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/MeasurementHistoryHttpTests.cs`
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/M39MeasurementHistoryDemo.cs`

**Modificados (del corte M3.9):**
- `StoreManagement/Interfaces/REST/EquipmentDeviceController.cs` (añadido endpoint GET measurements)
- `StoreManagement/Application/EquipmentTelemetryService.cs`
- `StoreManagement/Domain/Repositories/IEquipmentTelemetryRepository.cs`
- `StoreManagement/Infrastructure/Persistence/EquipmentTelemetryRepository.cs`
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/EquipmentTelemetryScenario.cs`
- `StoreManagement/README.md`
- `tools/StoreManagementPostgresDemo/Program.cs`
- `tools/StoreManagementPostgresDemo/README.md`

**Nota:** El working tree actual incluye cambios acumulados de cortes anteriores (M0.1-M3.8) además de M3.9. Debe distinguirse trabajo previo de trabajo realizado en cada corte. Se preservan sin commit.

## Decisiones Clave

- **Locales:** Pertenecen al usuario IAM creador, sin organizaciones/membresías/roles nuevos
- **Autorización:** Verificar titularidad y ascendencia en operaciones de equipos
- **Identidades:** NO equiparar UserId, ProfileId, HomeownerId, PropertyId, StoreId
- **Alertas:** Una por detección (BindingId/ReadingId), ACK conserva actor/fecha originales, no resuelve anomalía
- **Retirada:** Lógica impide nueva ingesta, conserva histórico
- **M3.9:** Consulta disponible al titular tras retirada dispositivo, sin Authority/AllowedDeviceIds

## Validación

Ver M3.9.md líneas 70-84 para detalles completos de pruebas (204/204 OK) y demostración PostgreSQL real.


## M3.10 — Company HTTP real (24 de octubre de 2026)

Recorrido funcional verificado con IAM/Profiles/Assets y PostgreSQL nuevos: dos complete/company 201 y login reales, Company Active/sujetos/portafolios persistidos, consulta individual titular 200 con DTO intacto y ajeno 404; cero modificaciones por GET. Regresión opt-in `--company-http` conservada, aparte de xUnit. Suite 204/204, focalizadas 11/11, build API 0 errores (NU1603 previo).

**Pendiente material:** el 400 reportado anteriormente NO se reprodujo con el helper actual; causa histórica sin confirmar. No se cambió payload, enum, validadores ni producción para hacerlo pasar. Se solicitó petición/error sanitizado original. No considerar cerrado ese diagnóstico. [Informe M3.10](../../tests/M3.10.md).

Precisión documental de M3.9: 11 archivos de código/soporte; 12 incluyendo tests/M3.9.md (4 nuevos + 8 modificados).


### Continuación M3.10 — capas y evidencia histórica

Verificado --company-layers: 400 JSON antes de acción, 400 de dominio/filtro, 409 por TaxId duplicado real y 201 válido; 22 campos en persistencia, evento y DTO correctos. TRX histórico encontrado sin cuerpo: apunta a línea de sign-up en fuente preservada, no acredita un fallo de complete/company. Input/motivo exactos no recuperables. Mapeos TaxId del usuario conservados; sin cambios de producción por Codex. 204/204, focalizadas 11/11; PostgreSQL opt-in aparte. [Detalle](../../tests/M3.10-layers.md).


## M3.11 — 25 de octubre de 2026

Corte funcional US19/migration M5–M6: from/to por fecha de creación del historial de alertas por equipo, sobre la ruta existente. Misma titularidad/ascendencia, DTO y orden; sin fechas compatible. Shared reutiliza el parser UTC de mediciones sin cambiar reglas. Sin DDL ni políticas nuevas de incidentes.

RED 3 fallidas → GREEN; focalizadas 9/9; suite 207/207, 0 fallidas/omitidas; PostgreSQL opt-in --alert-history pasado y recurso detenido/conservado. Build con exclusión CLI AppleDouble, 0 errores y NU1603 previo. Company se da por demostrado, 400 histórico no bloqueante; mapeos del filtro del usuario preservados. [Informe M3.11](../../tests/M3.11.md).


## M3.12 — 27 de octubre de 2026

Implementado GET /api/v1/stores/{storeId}/alerts con equipo opcional, respaldado por US19/migration M5–M6. Misma página/AlertResource con EquipmentId, fechas de creación UTC y filtros/orden de M3.11. Titularidad y ascendencia en servicio y EF; equipo incompatible 404, anónimo/Edge 401. Sin DDL, roles o reglas de incidentes nuevos.

RED 3 → focalizadas 18/18; suite 210/210 (207 anteriores +3), cero fallidas/omitidas. PostgreSQL nuevo --store-alert-history pasado y detenido/conservado; scopes nuevos y estado intacto. Build 0 errores con exclusión AppleDouble entrecomillada verificada; NU1603 previo. La sintaxis %3B anterior no excluyó los auxiliares en esta ejecución: corregida en el manual del ejecutor, sin borrar metadatos ni cambiar proyectos. Company demostrado y 400 histórico no bloqueante. [Informe M3.12](../../tests/M3.12.md).


## M3.13 — 27 de octubre de 2026

Único corte de aprovisionamiento: PUT existente de instalación lógica protegido por perfil Technician activo y asignación real a solicitud Installation/propiedad/dispositivo. Política aprobada expresamente por el usuario, separada de lectura/baja. Autorización en Application y lector ACL de Planning de solo lectura; comandos técnicos y DTO/esquema conservados. Sin bypass Admin/Edge.

RED 7 fallidas/1 válida; focalizadas 19/19; suite 218/218 (210 anteriores +8), cero fallidas/omitidas. PostgreSQL --installation-http aprobado: tres identidades reales, relaciones persistidas, éxito 200/evento único y rechazos sin mutaciones; repetición 409. Tres ensayos propios detenidos/conservados (dos intentos de fixture documentados, uno completo). Build 0 errores con exclusión AppleDouble verificada, NU1603 previo. [Informe M3.13](../../tests/M3.13.md).


## M3.14 — primer paso del punto 1 (27 de octubre de 2026)

Desde develop @42b9fb8: asignación HTTP existente protegida por técnico activo/asignado, solicitud/propiedad compatibles y selección explícita de stock libre, con política confirmada por el usuario. El puente deja de elegir el primer stock y exige SelectedDeviceId. Sin esquema/roles nuevos, commit/push o despliegue en este corte.

Base 218/218 ejecutada; RED 9 fallidas/1 válida; focalizadas 29/29; suite final 228/228, sin fallos/omisiones. PostgreSQL nuevo --assignment-http pasado: asignación e instalación por API, otro stock intacto, rechazos sin mutaciones/eventos. Recurso propio detenido y conservado. Build 0 errores con exclusión AppleDouble, NU1603 previo. Punto 1 sigue abierto: alta autorizada, selección persistida y reserva concurrente. [Informe](../../tests/M3.14.md).


## Continuidad vigente M3.15 — reserva del mismo dispositivo

Se reprodujo la carrera con dos POST autorizados leyendo InStock: 200/200, dos propiedades/eventos. Corregida con Status como token de concurrencia EF y 409 específico en assign; GREEN 200/409, una propiedad/evento, rollback de la perdedora. DDL offline idéntico; misma política, ruta y DTO. Focalizadas 29/29, suite 228/228, más una regresión PostgreSQL concurrente opt-in y compatibilidad asignación→instalación aprobadas. Tres destinos nuevos propios detenidos con datos retenidos. Build 0 errores con exclusión AppleDouble; NU1603 previo. Los 24 archivos previos con Fact/Theory están intactos. Sin stage/commit/push; develop @42b9fb8 y M3.14 preservados.

La pendiente de reserva en M3.14 queda atendida para competencia por **el mismo dispositivo**; punto 1 continúa abierto. Próximo incremento propuesto: definir política propia y proteger el registro HTTP de stock, sin heredar permisos de asignación/instalación. Después, selección persistida en workflow comercial; Planning sigue produciendo selección vacía y se deniega sin elegir stock implícito. También quedan carreras entre dispositivos distintos sobre una misma propiedad y frente a revocación/reasignación, no acreditadas aquí. Otros escritores EF de IoTDevice pueden ahora observar excepciones de concurrencia; no se cambia su manejo en este corte. No uso real, clientes o hardware acreditados. Ver tests/M3.15.md para archivos, comandos y recuperación exclusiva; M2.5 solo bloquea conciliación real.


## Continuidad vigente M3.16 — stock del técnico

Corrección expresa del usuario: el registro pertenece al técnico que gestiona su inventario; se descarta Admin/SuperAdmin como permiso de registro y se sustituye la selección de stock global de M3.14 por stock propio. IAM activo + Active Technician, subject concordante e inventario existente; actor del servidor, sin titular enviado por cuerpo. POST canonical /api/v1/iot-devices y alias anterior igualmente protegidos; GET my-stock paginado con cantidades total/disponible, GET propio por ID/Location, listados y contador HTTP legados aislados. Assign rechaza stock ajeno o sin titular y conserva relación de solicitud/propiedad y token concurrente.

Columna nueva nullable stock_technician_id e índice; sin atribuciones automáticas del stock previo. Borrador SQL 004 de Assets para revisión, **no aplicado a bases compartidas/existentes**. Destinos nuevos usan export actual cd69a8a040494b4aaa17f1c2eff4af9e922dd08d3438fd93588469384b66de6f. El código requiere esquema actualizado antes de un despliegue futuro autorizado; no se despliega aquí.

Verificación final 263/263 y 64/64 focalizadas, build 0 errores con exclusión AppleDouble. PostgreSQL real: registro propio→consulta→asignación→instalación, stock disponible 2→1, contadores activos 1/0, serial 201/409 y exclusión de asignación 200/409. Cinco destinos propios de la sesión detenidos/retienen datos; el primero fue un prototipo Admin descartado y no acredita la política vigente. Fixture M3.15 ahora usa dos solicitudes del mismo titular; DeviceAssignmentHttpTests actualiza stock válido a técnico explícito y agrega dos rechazos. No se debilitan las aserciones concurrentes; otras 23 fuentes previas Fact/Theory intactas.

El usuario autorizó commit/push de los cambios al terminar: publicación de M3.14/M3.15/M3.16 en develop de Hampcoders-Emergentes/project-backend-api, base anterior 42b9fb8. Consultar git log -1 develop y origen para el hash de la publicación; los informes anteriores conservan su estado histórico sin commit. Mantener metadatos Rider/AppleDouble, manifiestos y binarios fuera. No supone permiso de despliegue o cambios compartidos.

Punto 1 sigue pendiente de selección persistida y coordinación comercial en Planning/Subscriptions, transferencias y conciliación autorizada del stock sin titular. La consulta disponible permite revisar dispositivos, pero no valida automáticamente todos los materiales, cupos o requisitos del servicio ni reserva al consultar. No activar primer stock implícito para completar scheduling. M2.5 continúa pendiente solo para conciliación real. Pruebas, archivos y recuperación selectiva en tests/M3.16.md.
