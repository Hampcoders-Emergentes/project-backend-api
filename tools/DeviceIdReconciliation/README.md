# Conciliador local de IDs de relay/history

Auxiliar net9.0 sin paquetes externos ni referencia a la API. Lee un archivo JSON local explícito y escribe un informe a stdout. No consulta bases/URLs, no ejecuta el host habitual y no modifica inventario, configuración o permisos. **Supported no significa aprobado**; `grantsAccess` siempre es false.

## Ejecución desde la raíz del backend

```bash
dotnet build tools/DeviceIdReconciliation/DeviceIdReconciliation.csproj
dotnet tools/DeviceIdReconciliation/bin/Debug/net9.0/DeviceIdReconciliation.dll \
  --input tools/DeviceIdReconciliation/examples/synthetic.json \
  > /private/tmp/electrolink-reconciliation-report.json
```

Los ejemplos synthetic.json y ambiguous.json son íntegramente sintéticos. El segundo conserva duplicados/colisiones. El operador controla la redirección a un archivo de **informe**, nunca a appsettings/.env. No existen opciones --apply, --output, conexión o credenciales.

Salida 0: informe producido, aunque tenga pendientes. Salida 2: argumentos/entrada inválidos o archivo ilegible. Los errores no reproducen contenido/rutas de entrada y no se produce informe parcial por datos inválidos. Límite: 16 MiB por snapshot. No se ha validado procesamiento de exports ilimitados.

## Entrada, schemaVersion 1

Un JSON UTF-8 por entorno/corte, nombres camelCase exactos. Ver ejemplos completos incluidos.

| Campo raíz | Contenido |
|---|---|
| schemaVersion | 1 |
| snapshotId | Referencia estable no vacía del corte |
| environment | Nombre no secreto del entorno declarado por el operador |
| assets | Filas del inventario Assets |
| observations | IDs observados por Processing o utilizados por Edge para history |
| mappings | Asociaciones adicionales con evidencia explícita; puede estar vacío |

Cada fila:

- **assets:** recordId, internalId, serialNumber, propertyId (o null), status, evidenceRef; isDemo opcional, false por defecto.
- **observations:** recordId, source, deviceId exacto, propertyId (o null), evidenceRef; isDemo opcional. source admite solo processingStream, processingRelayCommand o edgeHistoryRequest.
- **mappings:** recordId, observedId, internalId, evidenceRef. El destino debe existir en inventario; no debe deducirse de similitud de nombres.

Los arrays deben existir, aunque estén vacíos. Las cadenas necesarias no pueden faltar/estar vacías; los IDs se conservan sin recortar espacios o cambiar mayúsculas. Las referencias de fila duplicadas se señalan: no se elige la última. Varias observaciones del mismo dispositivo con referencias de fila diferentes son normales.

Se rechazan campos desconocidos y propiedades JSON repetidas. **No incluir ApiKeyHash, claves, JWT, connection strings, datos personales o payloads completos.** evidenceRef apunta a un export/acta/copia sanitizada y su fila, idealmente con huella y clave de registro; la utilidad no abre ni autentica esas referencias. Una cadena no vacía no demuestra que la fuente sea autorizada.

## Resultado

Reporta snapshotId/environment, inputSha256 de los bytes leídos, identifiers, findings y grantsAccess=false. No emite allowedDeviceIds ni una configuración aprobada. Cada identificador conserva el deviceId exacto, matchedAssets, observations, mappings, sus evidenceRef, issues y hasRelayCommandObservation.

| Clasificación | Significado |
|---|---|
| Supported | Correspondencia exacta en inventario o mapping explícito a inventario, sin incidencias bloqueantes detectadas en la entrada |
| Unmatched | No hay destino de inventario; no se crea uno |
| Ambiguous | Identidades posibles múltiples, inventario duplicado/colisionado o contradicción de Property |
| DemoReview | Referencia demo explícita o exacta conocida; requiere revisión |
| Review | Referencias de fila duplicadas, mapping parcialmente sin destino o propiedad no verificada |

Si concurren varias incidencias, Ambiguous tiene prioridad sobre DemoReview, y todos los flags permanecen visibles. El reconocimiento demo es limitado a isDemo declarado y valores exactos observados en código: prop-demo, prop-edge-default y el fallback Edge dev-electrolink-esp32-01. No adivina demos por prefijos: marcar los demás explícitamente al preparar la entrada.

La fila de inventario sustenta la relación serie–ID; el informe **no reemplaza** la serie observada por el ID interno. No resuelve una colisión eligiendo el primer registro ni una contradicción usando defaults demo.

NoRelayCommandObservation significa ausencia de comandos con ese ID exacto **en el corte**, no que la base nunca tuviera comandos. No invalida por sí solo un dispositivo. InventoryOnly indica inventario sin observación operativa de ese ID; NoEdgeObservation pide revisar qué cadena usa realmente Edge.

Los mismos bytes producen el mismo informe, sin reloj/aleatoriedad/rutas del equipo en la salida. El motor ordena datos de forma estable y es independiente del orden de arrays. Si cambian bytes del archivo —incluso el formato— cambia inputSha256: la huella distingue archivos, no solo equivalencia lógica.

## Obtener entradas autorizadas

Esta tarea no autorizó ni ejecutó acceso real. Para un corte futuro:

1. Designar responsable y entorno/corte; autorizar específicamente exports en solo lectura o aportar copias sanitizadas ya autorizadas. No arrancar la API para obtenerlos.
2. Assets: proyectar solo ID interno, SerialNumber, AssignedPropertyId y Status con evidencia/clave de fila. Excluir credenciales, incluido ApiKeyHash. No derivar inventario autorizado de telemetría demo.
3. Processing: referencias de streams/comandos con DeviceId y PropertyId, claves de fila, procedencia y ventana/completitud del corte. No hacen falta mediciones, payloads completos ni mensajes MQTT.
4. Edge: ID exacto usado por history, hoy Reading.device_id; separar el fallback demo. Ese ID enviado por el cliente no acredita pertenencia.
5. Añadir mappings solo tras una comprobación explícita documentada —inventario, instalación o etiqueta revisada—. Si falta evidencia, dejarlo sin correspondencia; no completar por recorte, mayúsculas, parecido o prefijos.
6. Conservar exports, referencias y huellas junto al snapshot sanitizado. No mezclar ambientes bajo un nombre inventado: environment es una declaración, no una detección automática. No subir evidencia sensible a repositorios públicos.

## Revisar y mantener la lista

1. Revisar primero findings y todas las filas pendientes/ambiguas/demo. Corregir las fuentes mediante procesos autorizados, no desde este auxiliar.
2. Revisar también Supported: legitimidad, estado/propiedad vigentes y correspondencia con el ID exacto que consume history. Una relación con una serie no acredita que la cadena cumpla el contrato DeviceId/REST actual (p. ej. DeviceId exige dev-). Verificarlo sin reformatearla para hacerla encajar.
3. No descartar dispositivos por ausencia de comandos; comprobar ventana y alias. No inventar comandos para justificar un dispositivo.
4. Registrar por candidato, fuera de configuración: ID exacto propuesto, inventario destino, evidencia, entorno, motivo, persona revisora y decisión humana.
5. Solo en una tarea posterior autorizada, revisar/aplicar manualmente EdgeApi:RelayHistory:AllowedDeviceIds con alcance mínimo, denegación por omisión e historial de decisiones. La herramienta no concede esa aprobación.
6. Repetir corte y revisión ante altas, traslado, baja, cambio de ID o dudas. No mantener permisos por mera existencia de lecturas; conservar quién decidió cada modificación y el procedimiento de retirada.

**Responsable aún sin designar.** Se recomienda que el usuario designe una persona autorizada sobre inventario y configuración backend, con verificación de instalación cuando corresponda. No se considera aceptado ningún nombre/cargo. Todos los poseedores de la clave compartida siguen compartiendo el alcance; no se resuelven identidad de gateway, revocación o pertenencia humana por local.
