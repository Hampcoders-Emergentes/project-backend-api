# Alert Management mínimo — M3.4

Decisión del usuario: **una alerta por detección persistida**, clave (BindingId, ReadingId), sin agrupar lecturas diferentes. Solo detecciones frescas del recorrido vinculado nuevo crean alertas. Lecturas normales o atrasadas no crean alertas actuales. No se ejecuta backfill: repetir una lectura anterior que ya existía sin alerta no materializa historial automáticamente.

## Contrato humano

Base: `/api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/alerts`.

| Método | Sufijo | Resultado |
|---|---|---|
| GET | vacío | Página de alertas del equipo |
| GET | `/{alertId}` | AlertResource |
| POST | `/{alertId}/acknowledgement` | AlertResource con ACKNOWLEDGED, HTTP 200 |

Todas las rutas requieren identidad humana IAM y titularidad/ascendencia del equipo. El servicio usa la comprobación de jerarquía existente y el repositorio filtra por titular actual, vínculo, equipo, área y local. Otro usuario o ascendencia incorrecta → 404; sin humano, incluida clave Edge sola → 401. No se introducen roles, membresías o permisos globales Admin.

Filtros opcionales exactos `status=OPEN|ACKNOWLEDGED`, `severity=CRITICAL|NON_CRITICAL`. `page=1`, `pageSize=20` por defecto; tamaño 1–100 y offset Int32. Filtros/paginación inválidos → 400. Orden CreatedAtUtc descendente, Id ascendente como desempate; página `items,page,pageSize,hasMore`. No existe listado global sin alcance.

DTO: id estable `alert-<SHA256>` de la pareja de origen, bindingId, readingId, equipmentId, anomalyType, detectionSeverity, severity, capturedAtUtc, createdAtUtc, status, acknowledgedByUserId y acknowledgedAtUtc. El título del equipo/local se resuelve por sus referencias, no se copian datos de contacto o credenciales. El ID hash no acredita autorización; existe además índice único sobre la pareja original sin hash.

El POST de reconocimiento no requiere cuerpo. Cualquier cuerpo enviado se ignora y no aporta actor, fecha o estado: ambos valores de auditoría provienen del usuario IAM y UTC del servidor. Primera llamada: OPEN → ACKNOWLEDGED; repetición: 200 con **actor/fecha originales**. El SQL condicionado por OPEN preserva el primer ACK; no usa un reemplazo ciego de entidad. Las fechas de auditoría usan precisión de microsegundos.

## Severidades y responsabilidad

Se conserva DetectionSeverity original `Low/Medium/High/Critical`. Correspondencia de presentación con el diseño: `Critical → CRITICAL`; `Low/Medium/High → NON_CRITICAL`. Esta correspondencia no añade umbrales ni modifica el detector: High sigue visible como High. `Status` de alerta es OPEN/ACKNOWLEDGED, distinto de la severidad y de UNKNOWN/NORMAL/ANOMALY/STALE/DEVICE_UNVERIFIED de telemetría. No se implementa RESOLVED.

ACK solo registra atención humana. No cambia la medición, la detección, el vínculo o el estado del equipo. No demuestra resolución o fin del riesgo físico. Una alerta por lectura puede generar muchas alertas: no es agrupación de incidentes, histéresis o reapertura.

## Persistencia e integración transitoria

La responsabilidad está en `AlertManagement/`, con dominio, materializador, servicio humano, repositorio y controller propios. El materializador recibe un contrato explícito DetectedMeasurement; se llama únicamente cuando el nuevo servicio de ingesta acaba de detectar una anomalía fresca. No consume el outbox ni llama a canales externos o al servicio de relés legado.

Puente deliberado del monolito: `AlertMapping` añade `alert_management.alerts` al **mismo StoreManagementDbContext** utilizado por ambos repositorios del request. Stage solo añade la alerta pendiente; `EquipmentTelemetryRepository.Save` ejecuta un único SaveChanges para medición + alerta. No hay guardado independiente de la alerta al materializar. La operación PostgreSQL se probó con rollback de ambas escrituras ante una FK inválida. Separar contextos más adelante requerirá otra estrategia transaccional; no se puede conservar esta afirmación si se divide ese SaveChanges.

PK Id, índice único (BindingId,ReadingId), índice (EquipmentId,CreatedAtUtc), FK compuesta a medición y FK a equipo, ambas Restrict. El esquema anterior de StoreManagement no cambia. SQL aditivo 003 se encuentra junto a 001/002, para revisión: solo aplicado sobre una base NUEVA descartable. No aplicar estos borradores sobre bases existentes sin un baseline autorizado.

Reenvío idéntico de ingesta → 204 sin otra medición/alerta. Materialización repetida comprueba el origen, incluidas entidades pendientes; el índice único cierra la duplicación en almacenamiento. Bajo carrera de inserción, un intento puede devolver 409 por unicidad; no se promete que todos los reenvíos concurrentes obtengan 204. No hay reintentos habilitados ni compensación outbox. ACK usa una actualización SQL condicional inmediata y una lectura nueva; no se ensayó concurrencia exhaustiva.

Referencias de EF utilizadas: documentación oficial incluida en XML del paquete 9.0.5, miembros AutoTransactionBehavior.WhenNeeded y EntityFrameworkQueryableExtensions.ExecuteUpdateAsync. Este último actualiza inmediatamente y no sincroniza entidades rastreadas: por eso la consulta del DTO posterior es AsNoTracking. Referencias web equivalentes: https://learn.microsoft.com/ef/core/saving/transactions y https://learn.microsoft.com/ef/core/saving/execute-insert-update-delete . La evidencia de ejecución, comandos y recuperación está en [tests/M3.4.md](../tests/M3.4.md).


## M3.11 — filtrar el historial por fecha de creación

La ruta GET del equipo conserva DTO, permisos y filtros. Añade `from`/`to` opcionales sobre **CreatedAtUtc**, fecha de generación/detección de la alerta almacenada; no filtra CapturedAtUtc ni la fecha de ACK. Se aceptan ambos valores juntos, ISO con Z o ±HH:mm, hasta 6 decimales, normalizados a UTC; intervalo [from,to), from < to, máximo técnico 31 días. Codificar + como %2B. Sin fechas conserva toda la consulta anterior paginada. MVC trata los strings vacíos como ausentes; ambos vacíos equivalen a no filtrar. Un solo límite o fechas inválidas → 400.

Se combinan con status/severity; el equipo sigue seleccionado en la ruta. Ejemplo: `.../alerts?from=2026-01-01T00:00:00-05:00&to=2026-01-01T02:00:00-05:00&severity=CRITICAL&pageSize=20`. Resultado vacío: 200, items vacío y hasMore=false. No añade mensajes de UI, caché offline, resolución o notificaciones.

Se mantiene CreatedAtUtc descendente/Id ascendente, page=1/pageSize=20, máximo 100. Los predicados se aplican en EF antes de ordenar/paginar/materializar; se reutiliza (EquipmentId,CreatedAtUtc), sin DDL. Offset no congela datos entre peticiones: inserciones o cambios de estado concurrentes pueden desplazar páginas. El parser UTC antes exclusivo de mediciones ahora es un helper puro compartido; sus reglas no cambian. [Evidencia M3.11](../tests/M3.11.md).


## M3.12 — historial del local

`GET /api/v1/stores/{storeId}/alerts` consulta todos los equipos del local autorizado, sin recorrer sus equipos en cliente. Parámetros: equipmentId opcional; status, severity, from, to, page y pageSize con las mismas reglas de M3.11. El DTO existente ya contiene equipmentId: se conservan exactamente AlertResource (12 campos) y {items,page,pageSize,hasMore}.

```http
GET /api/v1/stores/{storeId}/alerts?from=2026-01-01T00:00:00-05:00&to=2026-01-01T02:00:00-05:00&status=OPEN&severity=CRITICAL&pageSize=20
GET /api/v1/stores/{storeId}/alerts?equipmentId={equipmentId}&page=1&pageSize=20
```

Autenticación humana IAM y titularidad del local, actor obtenido del servidor. Con equipmentId se valida su pertenencia al local a través de su área, incluso si no tiene alertas. Otro local del mismo titular también queda fuera. Local ajeno/inexistente o equipo incompatible/inexistente/mal formado/vacío → 404 {message:"Resource not found."}; un identificador de local mal formado conserva 400 de jerarquía. No se ignora el equipo inválido. Anónimo/solo Edge → 401. Filtros/fechas/página inválidos → 400; [ApiController] puede rechazar tipos de query antes del servicio. Sin resultados → 200 con items=[], hasMore=false.

from/to filtran CreatedAtUtc en UTC [from,to), máximo técnico 31 días, ambos o ninguno. Sin fechas no restringe periodo; ambos vacíos equivalen a ausencia como M3.11. Orden CreatedAtUtc DESC, Id ASC. EF aplica joins de pertenencia y filtros antes de Skip/Take/ToListAsync; solo se recorta el elemento extra para hasMore. Sin N+1 por equipo o nuevas tablas/índices. La lectura no usa autoridad de ingesta y conserva historia tras retirada. ACK, detección e inventario no cambian. Las rutas previas de equipo/detalle/ACK conservan contrato y permisos.

Offset no garantiza snapshot frente a nuevas alertas/cambios de estado. No acredita consumidor ejecutado, caché offline o producción. [Informe y recuperación M3.12](../tests/M3.12.md).
