# M3.1 — Contrato REST de locales, áreas y equipos

Política aprobada por el usuario: cada Store pertenece exclusivamente al usuario IAM creador (UserId us-…), sin organizaciones, miembros o roles nuevos. El actor proviene de Items["User"] cargado por RequestAuthorizationMiddleware; no se toma de ownerId, profileId o claims de pertenencia enviados por el cliente. Un rol Admin no permite operar locales ajenos. La clave Edge no habilita estas rutas.

## Rutas implementadas

| Método | Ruta | Cuerpo / resultado |
|---|---|---|
| POST | /api/v1/stores | {"name":"Synthetic restaurant","address":"Synthetic address"} → StoreResource |
| GET | /api/v1/stores | Página de locales del titular |
| GET | /api/v1/stores/{storeId} | StoreResource |
| POST | /api/v1/stores/{storeId}/areas | {"name":"Kitchen"} → AreaResource |
| GET | /api/v1/stores/{storeId}/areas | Página de áreas del local |
| GET | /api/v1/stores/{storeId}/areas/{areaId} | AreaResource |
| POST | /api/v1/stores/{storeId}/areas/{areaId}/equipment | {"name":"Oven"} → EquipmentResource |
| GET | /api/v1/stores/{storeId}/areas/{areaId}/equipment | Página de equipos del área |
| GET | /api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId} | EquipmentResource |

Respuestas DTO, sin entidades EF o navegaciones:

- StoreResource: id, name, address, ownerUserId (del usuario autenticado).
- AreaResource: id, storeId, name.
- EquipmentResource: id, areaId, name.
- Página: items, page, pageSize, hasMore. Defaults page=1 y pageSize=20; pageSize de 1 a 100, offset dentro de Int32. Orden por ID ascendente, no por fecha de creación. No hay conteo global ni consulta sin titular.

Names obligatorios de máximo 120 caracteres; address obligatorio de máximo 300. El dominio recorta espacios exteriores. Los cuerpos rechazan campos desconocidos, incluido ownerUserId: no hay asignación masiva de propietario. IDs generados por servidor: store-/area-/equipment- seguidos de UUID N (32 dígitos hexadecimales); no son IDs Property o IoTDevice.

POST exitoso devuelve 201 con Location hacia el GET correspondiente; GET exitoso devuelve 200. POST no es idempotente: reintentar una petición cuyo resultado se desconoce puede crear otro recurso. No se aceptan claves de idempotencia sin implementación.

Errores: 401 sin identidad humana válida (incluida clave Edge sola); 400 para cuerpo/IDs/paginación inválidos; 404 tanto para recurso inexistente como ajeno o con ascendencia incompatible. Validación automática usa ValidationProblemDetails de ApiController; errores controlados de dominio/servicio usan {"message":"..."}, conforme a convenciones existentes. Errores de persistencia no se convierten en éxito ni se absorben. No hay endpoints de traslado, cambio de titular, actualización o eliminación en este corte.

El servicio comprueba acceso al local antes de operar áreas y acceso al área dentro del local antes de operar equipos. El repositorio EF vuelve a restringir lecturas por titular y joins de ascendencia. Los nuevos endpoints conservan el formato de petición de los clientes anteriores: no sustituyen ninguna ruta heredada. M3.2 añade las rutas de vínculo y telemetría descritas más abajo; las rutas de jerarquía no modifican dispositivos heredados.

## Evidencia demostrable

StoreHttpTests usa TestServer con JwtBearer, TokenService, middleware IAM, atributo Authorize, DI modular, servicios y controller reales. Solo persistencia y consulta de usuarios son dobles sintéticos. Ejecuta las nueve rutas, dos usuarios Admin distintos, credenciales ausentes/máquina, relaciones cruzadas, entradas inválidas y paginación. No inicia el host habitual o jobs. El recorrido HTTP con dobles no acredita SQL o PostgreSQL ejecutados.

## Extensión M3.2 — dispositivo y telemetría del equipo

Para la base `/api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}`:

- `POST /device-binding`: cuerpo `{ "deviceId": "<ID interno Assets>", "edgeDeviceId": "<ID interno o serie exacta verificada>" }`; 201 y Location. No admite propietario, perfil, PropertyId o umbrales arbitrarios en el cuerpo.
- `GET /device-binding`: vínculo con ID interno, serie, ID Edge, LinkedAtUtc y ThresholdProvenance.
- `GET /telemetry`: equipmentId, state, binding y lastMeasurement. Sin lectura devuelve UNKNOWN con lastMeasurement nulo. El último registro se elige por captura descendente y ReadingId como desempate, no por llegada.

Requieren la identidad humana IAM y acceso a toda la ascendencia del equipo. Actor ajeno o padres incompatibles → 404. Dispositivo desconocido/no autorizado o relación ambigua → 403. Equipo/dispositivo/ID Edge ya vinculados → 409. No hay desvinculación, traslado o sustitución en este corte; los vínculos son explícitos e inmutables.

La autoridad verifica, en lectura: UserId → perfil Active y sujeto HomeOwner/Company activo mediante contratos Profiles; dispositivo Assets Installed; AssignedPropertyId de propiedad activa/no archivada del mismo ClientIdentity. ID interno y serie proceden del mismo agregado; el ID Edge debe ser exactamente uno de esos dos y no colisionar con otra identidad Assets. No se equipara StoreId con PropertyId ni se modifica la instalación heredada. Es legitimidad lógica para el vínculo, no prueba de traslado/instalación física en el nuevo local.

### Entrada de máquina conservada

Se conservan `POST /api/v1/iot/edge/readings`, el cuerpo IngestReadingResource y `GET /api/v1/iot/edge/devices/{deviceId}/thresholds`. La clave válida sigue siendo obligatoria. Para un ID vinculado, el nuevo servicio exige además coincidencia exacta en `EdgeApi:EquipmentReadings:AllowedDeviceIds` y vuelve a comprobar la autoridad actual del vínculo. Lista ausente/vacía → 403 para vinculados. No se cambió appsettings/.env ni se habilitaron IDs reales.

Una clave global autentica el canal, no identifica un gateway. Todos sus poseedores comparten el alcance configurado. Esta lista es independiente de RelayHistory:AllowedDeviceIds; no modifica relay/history ni acredita compatibilidad del Edge desplegado con ese flujo.

El ID Edge canónico está separado del ID interno y la serie. Un alias alternativo de un dispositivo ya vinculado se rechaza y no cae al legado. Un ID reservado en el alcance nuevo pero sin vínculo también se rechaza. IDs no vinculados ni reservados conservan el flujo heredado; sus defaults demo y otros riesgos previos **no quedan corregidos o aceptados** por M3.2. Para el recorrido vinculado no se crea stream, propietario, Property o vínculo demo.

Misma lectura (BindingId, ReadingId) con payload equivalente y captura UTC normalizada → 204 sin otro registro. El mismo ID con valores/origen distintos → 409 sin sobrescribir. No se promete deduplicación distribuida; la clave compuesta impide dos filas lógicas, pero las carreras de base/concurrencia no se ejecutaron. Un error SQL distinto de unicidad no se convierte en éxito.

### Medición y detección

Se guardan voltaje V, corriente A, factor de potencia adimensional, frecuencia Hz y origen exacto EdgeDevice/EdgeBufferReplay/EDGE. ActivePowerKw se calcula con la fórmula existente V×A×PF/1000; no es energía. `currentIna` adicional se sigue ignorando, como en el contrato anterior; no se inventan corriente residual o temperatura.

CapturedAtUtc y ReceivedAtUtc son distintos. Se conserva TimestampBasis: EXPLICIT_OFFSET cuando el JSON trae zona; EDGE_UTC_ASSUMED cuando no. El puente sin zona se basa en que el router Edge genera UTC y su DateTimeField puede perder el offset al persistir. No se afirma conocer una zona distinta ni se convierte el uptime del ESP32 en fecha. Se rechazan captura futura, fecha mínima, valores no finitos/fuera de rangos existentes, ReadingId vacío o de más de 100 caracteres y origen desconocido. La política de reloj/desfase requiere validación operativa.

El vínculo guarda snapshot de los ocho valores de Processing.ThresholdConfig.Default con procedencia `Processing.ThresholdConfig.Default:v1`: 220 V, 5000 W, 20 A, PF mínimo 0.85, 60 Hz, 10 minutos, normalLimit 0.20 A y alertLimit 0.60 A. El GET de umbrales vinculado entrega ese mismo snapshot. No se presenta como configuración personalizada por equipo ni se actualiza por eventos heredados de umbrales.

Se reutiliza ElectricalAnomalyRules, extracción del cuerpo de detección existente sin cambios. Ventana máxima 120 muestras anteriores a la captura evaluada; no hay contador de incidentes, AlertId, reconocimiento o resolución. NORMAL significa última muestra fresca sin detección de esas reglas, no certificación eléctrica. ANOMALY incluye tipo/severidad de la detección; no es Alert Management completo. STALE se aplica cuando la captura supera DisconnectionThresholdMin del snapshot, no cuando solo llegó recientemente. Lecturas ya atrasadas se guardan sin evaluación actual. DEVICE_UNVERIFIED indica muestra guardada cuya autoridad actual no se pudo volver a acreditar; no se presenta como NORMAL. El resultado histórico de detección no confirma el estado físico actual.

No se invoca AnomalyCommandService desde este flujo: esa clase conserva ramas de relé del legado. El nuevo recorrido solo llama a la función pura, sin relés, MQTT, eventos de negocio o proveedores.

### Validación PostgreSQL M3.3 y precisión temporal

El recorrido vinculado se comprobó con PostgreSQL local real, repositorios EF reales y nuevos servidores HTTP/sesiones de base; evidencia, comandos y límites en `tests/M3.3.md`. IAM/Profiles/Assets externos siguen doblados con registros sintéticos. No acredita inventario ni Edge real.

CapturedAtUtc se normaliza a microsegundos UTC antes de guardar/comparar, coincidiendo con la precisión PostgreSQL. El reenvío de la misma captura JSON con siete decimales conserva el 204; diferencias dentro del mismo microsegundo son indistinguibles en este almacenamiento. Un microsegundo distinto, valores u origen distintos conservan el conflicto de identidad. Se mantiene la marca temporal y el rechazo de capturas futuras antes de normalizar. No cambian rutas, payloads, roles, alcance Edge ni esquema.

## Extensión M3.4 — alertas y atención humana

Las detecciones nuevas frescas del recorrido vinculado crean una alerta OPEN por BindingId/ReadingId. Se añaden rutas anidadas `/alerts`, `/alerts/{alertId}` y `/alerts/{alertId}/acknowledgement`; contrato completo en [AlertManagement/README.md](../../../AlertManagement/README.md). ACK registra atención, conserva el primer actor/UTC y no modifica telemetría ni actúa sobre el equipo. No hay backfill, agrupación, resolución o notificaciones. El contrato Edge y las rutas anteriores se conservan.


## M3.9 — historial de muestras de un equipo

`GET /api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/measurements`

Consulta humana aditiva: reutiliza autenticación IAM, titular exclusivo del Store y ascendencia Store→Area→Equipment. Ajeno o jerarquía incompatible → 404 genérico; anónimo o solo Edge → 401. No añade roles ni verifica la elegibilidad actual de ingesta: el historial permanece disponible tras la retirada. Equipo autorizado sin vínculo/muestras → 200 con página vacía.

Parámetros requeridos `from` y `to`: ISO `yyyy-MM-ddTHH:mm:ss[.ffffff]Z` o con offset `±HH:mm` (máximo seis decimales, sin zona implícita). Normalizados a UTC, sobre CapturedAtUtc; inicio incluido y fin excluido. `from < to`, intervalo máximo 31 días, sin redondear límites. Codificar `+` como `%2B` en la URL. `page=1`, `pageSize=20` por defecto, page positivo y pageSize 1–100, offset dentro de Int32. Parámetros inválidos → 400 según las convenciones existentes.

Página `{items,page,pageSize,hasMore}` sin total global. Orden ascendente por CapturedAtUtc y luego ReadingId con collation PostgreSQL C; el ID es único dentro del vínculo inmutable. EF ejecuta joins de pertenencia/ascendencia, filtro temporal, orden y OFFSET/LIMIT en PostgreSQL; se materializan como máximo pageSize+1 filas. Reutiliza el índice (BindingId,CapturedAtUtc) y la PK (BindingId,ReadingId), sin nuevo DDL. Alto volumen/offset requiere medición posterior.

Los items solo contienen campos almacenados: readingId, capturedAtUtc, receivedAtUtc, timestampBasis, voltage (V), current (A), powerFactor (adimensional), frequency (Hz), source, detectedAnomalyType y detectedSeverity. No energía/kWh, costos, agregaciones ni ActivePowerKw derivado. Detección nula no certifica normalidad ni evaluación: puede ser muestra atrasada sin evaluación. Detección/severidad histórica no son estado actual ni ACK/resolución de la alerta.

Paginación por offset, coherente con las páginas existentes: orden estable sobre datos sin cambios, **sin snapshot entre solicitudes**. Una muestra tardía dentro del intervalo puede desplazar páginas y provocar repeticiones u omisiones al continuar. Para reconciliar, repetir el intervalo desde page=1 y deduplicar por ReadingId dentro de este equipo/vínculo. Un `to` fijo limita la ventana, pero no impide llegadas tardías dentro de ella. No se promete exportación transaccional.

Ejemplo sanitizado: `.../measurements?from=2026-01-01T00:00:00-05:00&to=2026-01-01T02:00:00-05:00&page=1&pageSize=20` equivale a 05:00Z–07:00Z. Evidencia, límites y recuperación: [tests/M3.9.md](../tests/M3.9.md).
