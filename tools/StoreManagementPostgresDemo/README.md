# Demostraciones PostgreSQL aisladas

No ejecutar Program, EnsureCreated/Migrate o workers. El auxiliar requiere Docker local, una imagen PostgreSQL/PostGIS ya disponible y autorizada, y crea siempre otro contenedor/base/UUID. Nunca descarga imágenes ni reutiliza las bases anteriores. Solo publica loopback y guarda credenciales sintéticas aleatorias en un manifiesto 0600, fuera del repositorio.

- M3.3–M3.4: ejecutar el DLL con un manifiesto preparado sin `--legacy-schema`. La identidad/inventario externos siguen sintéticos con dobles; módulo nuevo y PostgreSQL son reales.
- M3.5: exportar el DDL de AppDbContext **offline**, revisarlo, preparar un destino nuevo con ese DDL y ejecutar `--real-backend`. IAM/Profiles/Assets usan DI/servicios/repositorios reales.

## Modo M3.5

Con SDK/runtime .NET 9 disponible (no sustituirlo silenciosamente por .NET 10), desde la raíz:

```bash
M35_DOTNET=/private/tmp/electrolink-dotnet9/dotnet
"$M35_DOTNET" build tools/StoreManagementPostgresDemo/StoreManagementPostgresDemo.csproj --no-restore
M35_REVIEW_DIR="$(mktemp -d "${TMPDIR:-/tmp}/electrolink-m35-review.XXXXXX")"
M35_LEGACY_SQL="$M35_REVIEW_DIR/legacy.generated.sql"
"$M35_DOTNET" tools/StoreManagementPostgresDemo/bin/Debug/net9.0/StoreManagementPostgresDemo.dll \
  --export-legacy-schema "$M35_LEGACY_SQL"
```

El exportador rechaza sobrescribir un archivo. No abre conexión: el modelo real genera DDL para revisión, con PostGIS y todas sus tablas (44 en el modelo comprobado), aunque solo se ejercite IAM/Profiles/Assets. No son migraciones para bases existentes. Revisar el archivo antes de continuar. No añadir triggers/jobs/scripts ajenos. La imagen local empleada en el ensayo M3.5 ya incluía PostGIS (`cbdd40bde138`), por lo que no se instaló ni descargó otra plataforma.

```bash
rg -n '^CREATE (EXTENSION|TABLE)|^ALTER |^DROP |^INSERT |^UPDATE |^DELETE |^COPY ' "$M35_LEGACY_SQL"
python3 tools/StoreManagementPostgresDemo/disposable.py prepare \
  --image cbdd40bde138 --legacy-schema "$M35_LEGACY_SQL"
```

Este comando verifica identidad de contenedor/base nueva vacía, aplica DDL heredado + 001/002/003 en una transacción y reporta solo la ruta privada del manifiesto y destino sin credenciales. El SQL heredado se aplica únicamente si se solicita explícitamente. Establecer `M35_MANIFEST` con la ruta recién reportada, sin imprimir su contenido:

```bash
"$M35_DOTNET" tools/StoreManagementPostgresDemo/bin/Debug/net9.0/StoreManagementPostgresDemo.dll \
  --real-backend "${M35_MANIFEST:?Manifiesto privado del destino nuevo}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop \
  "${M35_MANIFEST:?Manifiesto privado propio}"
```

El ejecutor rechaza datos previos. Para repetir un recorrido que ya creó usuarios se requiere prepare nuevo; no truncar tablas. Stop comprueba propiedad/imagen/base/loopback y conserva datos, sin borrar contenedores o volúmenes. No usar limpieza Docker global. Consultar [tests/M3.5.md](../../tests/M3.5.md) para evidencia, límites y recuperación.

## Fronteras del modo real

Registro, completar perfil y login se realizan por HTTP. Los JWT se obtienen del endpoint real; middleware y consulta de usuarios acceden a PostgreSQL. Se reutilizan AddIamContextServices, AddProfilesContextServices y AddAssetsContextService sin construir/arrancar su WebApplicationBuilder; sus descriptores se incorporan al TestServer. AppDbContext usa UseNpgsql/UseNetTopologySuite y el mismo interceptor de fechas que el contexto de producción; StoreManagement conserva su DI real.

Preparación técnica declarada: crear propiedad y añadirla al portafolio, registrar, asignar e instalar lógicamente un dispositivo mediante servicios reales de aplicación en un scope. El TechnicianId es una referencia sintética opaca aceptada por RecordDeviceInstallationCommand; no se crea un técnico autenticado ni se demuestra autorización HTTP del instalador o instalación física. Ningún repo se sustituye por un diccionario.

MediatR 11 es real, con los handlers seleccionados UserRegistered→Profiles, ProfileCompleted→Assets y UserSignedIn (registro). No hay escaneo global de handlers: los de suscripciones/Analytics/proveedores y otros flujos no se ejercitan. Esto no equivale a iniciar el host completo. La existencia del portafolio esperado se comprueba para detectar fallos que el handler Assets puede capturar internamente.

Dobles restantes: centinelas que fallan para almacenamiento de archivos, HTTP saliente y dependencias de ingesta/relés legados. Se comprueba que no hubo invocaciones. No son dobles de identidad, Claims de perfil, propiedad o dispositivo. No se encienden workers, outbox, notificaciones, MQTT o hardware. La clave global Edge sigue siendo una credencial sintética del canal; no se ejecuta Edge real.

## Modo M3.6 — baja lógica sin borrar histórico

Crear **otro** destino siguiendo la exportación offline, revisión y `prepare --legacy-schema` anteriores. No utilizar un manifiesto poblado de M3.5. Con `M36_MANIFEST` apuntando al manifiesto privado recién creado:

```bash
/private/tmp/electrolink-dotnet9/dotnet \
  tools/StoreManagementPostgresDemo/bin/Debug/net9.0/StoreManagementPostgresDemo.dll \
  --revocation "${M36_MANIFEST:?Manifiesto privado del destino nuevo}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop \
  "${M36_MANIFEST:?Manifiesto privado propio}"
```

Este modo conserva el recorrido M3.5 y añade DecommissionDeviceCommand mediante servicio/UoW reales después de persistir y reconocer una alerta. No modifica filas directamente ni ejecuta una orden física. Verifica autoridad denegada desde scopes nuevos, clave Edge/allowlist sin cambios, lectura nueva posterior → 403, DEVICE_UNVERIFIED y comparación íntegra de mediciones, detección, vínculo y DTO de alerta/ACK. Conserva el acceso humano del titular y el rechazo a usuario ajeno o solo Edge.

Se habilita además el handler real de Assets de DeviceDecommissionedEvent, que solo registra el hecho. El handler Processing que pausa streams legados sigue excluido: no se acredita su efecto ni el host completo. Todos los centinelas siguen activos. No modifica la política de autorización, los contratos ni el esquema. Evidencia y límites en [tests/M3.6.md](../../tests/M3.6.md).

## Modo M3.7 — autorización HTTP de baja

Después de exportar/revisar el esquema heredado y hacer **prepare nuevo** con `--legacy-schema`, ejecutar:

```bash
/private/tmp/electrolink-dotnet9/dotnet \
  tools/StoreManagementPostgresDemo/bin/Debug/net9.0/StoreManagementPostgresDemo.dll \
  --retirement-http "${M37_MANIFEST:?Manifiesto privado del destino recién creado}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop \
  "${M37_MANIFEST:?Manifiesto privado propio}"
```

Monta el controller real de IoTDevices solo en esta composición optativa. Prueba sin autenticación, solo Edge, usuario ajeno e ID inexistente **antes** de la baja válida; cada rechazo se compara desde otro scope y debe emitir cero eventos. Un observador de pruebas cuenta publicaciones sin sustituir servicio/repositorios/handler reales. Después exige baja titular 204 y repetición sin mutación/eventos, y reutiliza las comprobaciones históricas esenciales de M3.6. Los modos anteriores conservan sus valores por defecto.

Política confirmada y contrato del endpoint: [Assets/README.md](../../Assets/README.md). No reutilizar las bases RED/GREEN pobladas de este ensayo ni ejecutar Program. Ver [tests/M3.7.md](../../tests/M3.7.md) para resultados y límites.


## Modo M3.8 — consulta individual

Reutiliza el esquema legado generado offline y revisado y el auxiliar `disposable.py prepare` descritos arriba, siempre con destino NUEVO y propio. No reutilizar las bases pobladas RED/GREEN ni imprimir el manifiesto.

```sh
/private/tmp/electrolink-dotnet9/dotnet run --no-restore --project tools/StoreManagementPostgresDemo -- \
  --device-read "${M38_MANIFEST:?Manifiesto privado del destino recién creado}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M38_MANIFEST}"
```

Dos usuarios IAM/HomeOwner reales, una propiedad y dos dispositivos sintéticos. Ocho consultas con estado HTTP, contenido, comparación del inventario entre scopes nuevos y observador de notificaciones Assets. El retirado es un fixture mínimo por comando interno, sin repetir el recorrido HTTP de baja/ingesta. No ejecuta Program, workers, MQTT ni proveedores. Se conservan los datos al detener. Ver [tests/M3.8.md](../../tests/M3.8.md).


## Modo M3.9 — historial de mediciones

Compilar con el SDK existente y --no-restore. Generar/revisar el DDL legado offline y usar `disposable.py prepare --legacy-schema` como en los modos anteriores, **siempre destino nuevo vacío y propio**. No reutilizar los manifiestos poblados de otras tareas.

```sh
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- \
  --measurement-history "${M39_MANIFEST:?Manifiesto privado del destino NUEVO}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M39_MANIFEST}"
```

Reutiliza RealBackendScenario y los casos HTTP de historial. Dos usuarios IAM/HomeOwner reales; dos jerarquías, una propiedad/dispositivo/vínculo, seis muestras sintéticas y una alerta ACK sembradas explícitamente por repositorio EF. No pretende repetir ni acreditar ingesta/detección/ACK. Consulta dos páginas desde scopes nuevos, comprueba límites y offsets, rechazo por titularidad/ascendencia/Edge, parámetros inválidos y SQL paginado real. Retira mediante comando técnico solo como fixture, comprueba autoridad de ingesta revocada e histórico idéntico. Compara estado persistido y contador de notificaciones antes/después de las ventanas de lectura. Sin Program, EnsureCreated/Migrate, jobs, proveedores o hardware. Stop conserva los datos. Manifiestos/credenciales fuera de Git; no imprimirlos. Ver [tests/M3.9.md](../../tests/M3.9.md).


## M3.10 — Company HTTP real

Modo optativo `--company-http`, separado de las 204 pruebas xUnit. Usar únicamente un manifiesto del auxiliar existente para un PostgreSQL **nuevo, vacío y propio**, con DDL legado revisado como en los modos anteriores. No reutilizar los destinos ya poblados.

```sh
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- \
  --company-http "${M310_MANIFEST:?Manifiesto privado de un destino NUEVO}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M310_MANIFEST}"
```

Primero llama al helper Company sin modificar sus datos/construcción: signup, complete/company esperado 201 y login reales. Solo después prepara inventario. Dos Company Active y sus sujetos/portafolios se verifican desde otro scope. Propiedad/dispositivo sintéticos se preparan por servicios reales; consulta titular 200 con los ocho campos existentes y ajeno 404, comparando inventario/outbox/notificaciones. Sin host habitual, workers, proveedores o hardware. Stop conserva los datos.

El observador opt-in de complete/company imprime estado y Content-Type; ante fallo solo campos de error sanitizados, sin requests, tokens o manifiestos. Mantiene la aserción 201: no acepta 400 como éxito. Con el helper actual se observó 201; el 400 histórico no se reprodujo y su causa queda pendiente de la petición/cuerpo original. No atribuirlo a Small, DNI o TaxId por conjetura. Ver [tests/M3.10.md](../../tests/M3.10.md).


### Company: comprobación de capas

`--company-layers <manifiesto-nuevo>` amplía el mismo recorrido de --company-http. Observador de filtros/eventos solo en TestServer: distingue model binding de acción/controller y servicio/dominio. Dos 400 controlados (enum inválido y teléfono inválido), unicidad TaxId 409, luego completado válido. Compara los 22 campos de entrada con persistencia, evento ProfileCompleted y DTO; conserva también titular 200/ajeno 404. No son reproducciones del incidente histórico.

La salida Company ahora distingue sign-up de complete/company. El TRX antiguo apunta a una aserción que corresponde a sign-up en la copia guardada; no conserva el cuerpo HTTP original. Ver [M3.10-layers](../../tests/M3.10-layers.md).

Si el volumen externo conserva AppleDouble de ediciones ajenas, compilar sin borrarlos:

```sh
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore '-p:DefaultItemExcludesInProjectFolder="**/.*/**;**/._*"'
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- \
  --company-layers "${COMPANY_LAYERS_MANIFEST:?Destino NUEVO del auxiliar}"
```

La opción MSBuild es temporal, conserva exclusiones de carpetas ocultas y omite auxiliares ._*; no modifica proyectos. Verificar exit code de build antes de usar --no-build. El modo crea fixtures nuevos, no acepta bases pobladas; detener con disposable.py stop conservando datos.


## M3.11 — historial de alertas por fechas

Un solo modo nuevo `--alert-history <manifiesto>`. Después de compilar exitosamente (fail-fast), preparar destino PostgreSQL nuevo/vacío/propio con el auxiliar y DDL legado revisado, como en M3.10. Ejecutar y detener exclusivamente ese destino:

```sh
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore '-p:DefaultItemExcludesInProjectFolder="**/.*/**;**/._*"'
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- \
  --alert-history "${M311_MANIFEST:?Manifiesto privado del destino NUEVO}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M311_MANIFEST}"
```

IAM/Profiles, jerarquía y repositorios de alertas/mediciones reales. Fixture explícito: dos jerarquías, un vínculo con referencias heredadas sintéticas, seis muestras/alertas y un ACK; no acredita instalación o ingesta física. Consulta intervalo de creación, páginas y filtros combinados, compatibilidad sin fechas, accesos denegados/400 y aislamiento directo del repositorio. Compara desde scopes nuevos todo el estado relevante y notificaciones, sin imprimir filas o digests. Sin Program API, jobs o proveedores. No reutilizar bases pobladas; stop conserva datos. Ver tests/M3.11.md.


## M3.12 — historial de alertas del local

Nuevo modo opt-in `--store-alert-history <manifiesto-nuevo>`, distinto de --alert-history (M3.11). Usa TestServer/IAM/Profiles/repositorios EF reales, dos usuarios sintéticos, tres locales y cuatro equipos en cuatro áreas; cuatro vínculos de referencia sintética y 24 muestras/alertas (cuatro ACK). Los dos equipos del local consultado tienen 12 alertas. No acredita instalación o inventario real: no crea IoTDevices ni usa autoridad de ingesta.

Secuencia fail-fast (no ejecutar las líneas siguientes si falla build):

```sh
set -e
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore '-p:DefaultItemExcludesInProjectFolder="**/.*/**;**/._*"'
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --export-legacy-schema /private/tmp/electrolink-m312-legacy.NUEVO.sql
# Revisar el SQL exportado ANTES de preparar destino; no sobrescribir/reutilizar archivos o bases anteriores.
python3 tools/StoreManagementPostgresDemo/disposable.py prepare --image cbdd40bde138 --legacy-schema /private/tmp/electrolink-m312-legacy.NUEVO.sql
# Asignar M312_MANIFEST a la ruta privada recién emitida, sin imprimir contenido/secretos.
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --store-alert-history "${M312_MANIFEST:?Destino NUEVO vacío propio}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M312_MANIFEST}"
```

El modo verifica marcador/UUID/destino y ausencia de datos relevantes antes de crear fixtures. Consulta HTTP con scopes nuevos; verifica límites de creación, filtros antes de paginar, tres páginas con empates, EquipmentId y DTO exactos, aislamiento entre locales/actores, equipo incompatible 404 y tipos/filtros inválidos. Reutiliza los casos HTTP del corte y el snapshot de solo lectura de M3.11. Repositorio consultado directamente para aislamiento y composición. Compara alertas/ACK, mediciones, vínculos, jerarquía, IoTDevices y outbox; centinelas externos y delta eventos Assets en cero. Sin cambios del auxiliar ni DDL M3.12.

Corrección de invocación AppleDouble comprobada en M3.12: `%3B` dentro del argumento anterior no excluyó auxiliares en esta ejecución. Usar el valor entrecomillado mostrado arriba, que conserva los mismos patrones **/.*/** y **/._*. Se compararon Compile items, sin quitar fuentes legítimas o borrar metadatos. No se cambiaron csproj. El build ordinario puede fallar con CS2015 en este volumen. Datos del destino propio se conservan al detener; no volver a ejecutar fixtures sobre él. Ver tests/M3.12.md.


## M3.13 — instalación lógica HTTP

Modo opt-in `--installation-http <manifiesto-nuevo>`. Usa el mismo auxiliar verificado y la imagen local existente, sin pulls o bases anteriores. Compilar primero y detener la secuencia si falla:

```sh
set -e
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore '-p:DefaultItemExcludesInProjectFolder="**/.*/**;**/._*"'
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --export-legacy-schema /private/tmp/electrolink-m313-legacy.NUEVO.sql
# Revisar el export antes de aplicarlo; elegir ruta nueva.
python3 tools/StoreManagementPostgresDemo/disposable.py prepare --image cbdd40bde138 --legacy-schema /private/tmp/electrolink-m313-legacy.NUEVO.sql
# M313_MANIFEST debe ser el manifiesto privado recién emitido; no imprimir su contenido.
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --installation-http "${M313_MANIFEST:?Destino nuevo propio y vacío}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M313_MANIFEST}"
```

Un HomeOwner y dos técnicos registrados/completados/autenticados por HTTP real; perfiles y dos inventarios técnicos reales. Propiedades, registro y asignación de dispositivo son precondiciones técnicas con servicios reales. Solicitud Installation y asignación se preparan con fábricas de dominio, repositorios/UoW reales y metadata sintética, sin ejecutar matching/scheduling. El estado Installed nunca se prepara directamente: se obtiene por el PUT protegido. Las instancias owned de ClientIdentity se crean por propietario EF, sin compartir referencia entre agregados.

Verifica técnico asignado 200, otro técnico activo 404, suplantación 404, anónimo/Edge 401, propiedad incompatible/dispositivo inexistente y solicitud reactivada 404, repetición 409. Scopes nuevos verifican persistencia y snapshots antes/después de cada rechazo; un evento Assets por éxito, ninguno por rechazo/repetición. Handler Assets de logging real; Processing/Subscriptions y sus efectos quedan excluidos explícitamente. Streams/outbox en cero; centinelas externos en cero. No acredita hardware, clientes reales, matching automático o aprovisionamiento completo. Manifiestos poblados se conservan detenidos y no se reutilizan. Ver tests/M3.13.md.


## M3.14 — asignación por HTTP y continuidad de instalación

Modo opt-in `--assignment-http <manifiesto-nuevo>`. Reutiliza el fixture real de M3.13 mediante opción explícita: registra stock técnicamente, pero reemplaza AssignDeviceToPropertyCommand de preparación por el POST HTTP autorizado. Después ejecuta las comprobaciones de instalación protegida de M3.13 sobre ese dispositivo asignado por API. El modo histórico --installation-http conserva su comportamiento por defecto.

```sh
set -e
/private/tmp/electrolink-dotnet9/dotnet build tools/StoreManagementPostgresDemo --no-restore '-p:DefaultItemExcludesInProjectFolder="**/.*/**;**/._*"'
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --export-legacy-schema /private/tmp/electrolink-m314-legacy.NUEVO.sql
# Revisar export antes de preparar; elegir archivo nuevo.
python3 tools/StoreManagementPostgresDemo/disposable.py prepare --image cbdd40bde138 --legacy-schema /private/tmp/electrolink-m314-legacy.NUEVO.sql
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --assignment-http "${M314_MANIFEST:?Manifiesto privado de destino nuevo}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M314_MANIFEST}"
```

Tres usuarios/perfiles reales (cliente y dos técnicos), dos propiedades, solicitud/asignación técnica reales y dos dispositivos en stock. Se selecciona uno por ID; el otro conserva InStock sin asignación. Usuario técnico ajeno, anónimo, Edge, solicitud/propiedad incompatibles y dispositivo desconocido no modifican estado ni emiten eventos. Éxito crea relación por HTTP, DTO exacto y evento único; repetición secuencial 409 sin nuevos efectos. Scopes nuevos verifican dispositivo, propiedad, solicitud y stock restante. Un evento de scheduling sin ID falla visiblemente sin tocar inventario. Después se demuestra instalación HTTP y su compatibilidad con rechazos previos.

Precondiciones de registro de stock, propiedades y preparación Planning siguen técnicas. Se ejecutan handlers Assets de logging de asignación/instalación y observador existente; no Processing/Subscriptions, matching, jobs o hardware. Sin concurrencia demostrada: sigue pendiente reserva atómica. No reutilizar destinos poblados; detener conserva datos. No imprimir manifiestos. Ver tests/M3.14.md.


## M3.15 — carrera de asignación HTTP

Tras exportar/revisar el DDL offline y preparar un destino nuevo vacío propio con el auxiliar anterior:

```bash
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --assignment-race "${M315_MANIFEST:?Manifiesto privado nuevo}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M315_MANIFEST:?Manifiesto privado propio}"
```

Compilar primero con la exclusión AppleDouble documentada. No reutilizar un manifiesto ya poblado. Este modo agrega un SaveChangesInterceptor solo al TestServer para que dos contextos lean InStock antes de guardar y para ordenar las confirmaciones, sin sleeps. La preferencia del ganador es coordinación de prueba, no prioridad de negocio. Usa IAM/Profiles, repositorios y transacciones Npgsql reales; verifica 200/409, rollback de propiedad perdedora, un evento, stock sobrante libre y repetición rechazada. No inicia host habitual, workers o comunicaciones. Es una regresión PostgreSQL opt-in independiente de las 228 pruebas xUnit. [Informe, límites y recuperación](../../tests/M3.15.md).


## M3.16 — alta y consulta del stock propio del técnico

La política vigente es Technician activo con inventario existente, corregida por el usuario; la propuesta Admin/SuperAdmin fue descartada. Generar y revisar **otro** DDL offline con el modelo actual: incluye stock_technician_id nullable y su índice; no usar el export anterior sin esa columna. Preparar siempre destino nuevo vacío con ese DDL y 001/002/003, sin aplicar borradores a destinos existentes.

```bash
/private/tmp/electrolink-dotnet9/dotnet run --no-build --no-restore --project tools/StoreManagementPostgresDemo -- --registration-http "${M316_MANIFEST:?Manifiesto privado nuevo}"
python3 tools/StoreManagementPostgresDemo/disposable.py stop "${M316_MANIFEST:?Manifiesto privado propio}"
```

Compilar antes con la exclusión AppleDouble entrecomillada ya documentada. El modo usa tres clientes/técnicos reales IAM/Profiles, inventarios creados por ProfileCompleted, dos cuentas staff preparadas mediante servicio técnico para comprobar rechazo, una propiedad/solicitud/asignación sintéticas persistidas, y un dispositivo técnico legado sin procedencia. El alta de los otros tres dispositivos se realiza por HTTP; dos inserciones del mismo serial compiten tras ambos lookups, coordinadas por interceptor de prueba, con 201/409 y una fila/evento.

Comprueba titular desde actor pese a IDs forjados, lectura propia/Location, conteos y páginas SQL, alias/listados/contador activo propios, rechazo de stock ajeno o NULL en una solicitud por lo demás válida, asignación propia→instalación lógica y disponible 2→1. Admin/SuperAdmin sin perfil técnico, cliente, Edge, anónimo y técnico IAM suspendido rechazados; sin comunicaciones. No verifica aprovisionamiento HTTP de cuentas staff o matching comercial.

Para --assignment-race de M3.15 ahora compiten dos solicitudes del **mismo técnico propietario**, con dos contextos leyendo InStock: conserva 200/409, rollback de propiedad y evento único. El antiguo supuesto de dos titulares distintos sobre stock global quedó sustituido por la corrección expresa del usuario; no se elimina la aserción de exclusión. --assignment-http registra técnicamente dispositivos con propietario explícito de fixture antes de probar HTTP assign/install. [Evidencia vigente](../../tests/M3.16.md).


## M3.19 — propiedad de la instalación Enterprise por el Company titular

Modo opt-in `--installation-property-http <manifiesto-nuevo>`. Requiere un destino nuevo y vacío (export revisado + 001/002/003); no reutilizar manifiestos poblados.

- **Real:** IAM/JWT, Profiles, Assets, Planning y EF/Npgsql. Dos Company y un técnico se registran y completan por HTTP.
- **Preparación técnica:** las propiedades se crean con el servicio de Assets y la receta Installation con el dominio de catálogo.
- **Creación de la solicitud:** con el handler real de Planning. Solo la fachada de Subscriptions es un registrador.
- **Matching:** `ServiceAssignmentCommandService` + `HybridMatchingService` reales. El proveedor de IA es un centinela que bloquea y cuenta, sin red, por lo que se usa el fallback determinista existente.
- **Configuración Npgsql:** el modo activa `EnableDynamicJson()` en su proceso, igual que `Program.cs`.

Verifica:
- la persistencia de PendingInstallation con el Client Company;
- los rechazos 401/403/404/400 sin efectos;
- la carrera 200/409 con un interceptor;
- la repetición 409;
- la aceptación del reader;
- assign e install del técnico sobre su stock;
- NoCandidates persistido;
- 0 llamadas a cupo, flujo legado o HTTP saliente.

No ejecuta Stripe, jobs, MQTT ni Program. Evidencia en [tests/M3.19.md](../../tests/M3.19.md).

## M3.22 — titularidad en `PropertiesController`

Modo opt-in `--property-ownership-http <manifiesto-nuevo>`, en un destino vacío.

- Un HomeOwner y un Company reales se registran por HTTP y crean sus propiedades por POST.
- Verifica la lectura propia (200), el acceso cruzado (404 sin cambios en filas), el prefijo cruzado (404), anónimo y Edge (401) y la desactivación propia persistida.
- Sin servicios externos. Evidencia en [tests/M3.22.md](../../tests/M3.22.md).

## M3.23 — titularidad en `PropertyPortfoliosController`

Modo opt-in `--portfolio-ownership-http <manifiesto-nuevo>`, en un destino vacío.

- HomeOwner y Company reales; sus portfolios los crea el handler real de `ProfileCompleted`.
- Verifica que el titular añade y quita su propiedad, y que otro owner, una propiedad ajena o un prefijo cruzado dan 404 sin cambios en las entradas, y Edge 401.
- Evidencia en [tests/M3.23.md](../../tests/M3.23.md).

## M3.24 — titularidad en `TechnicianInventoriesController`

Modo opt-in `--inventory-ownership-http <manifiesto-nuevo>`, en un destino vacío.

- Dos técnicos reales; sus inventarios los crea el handler real de `ProfileCompleted`.
- Verifica la lectura propia (200), la creación repetida (409), el acceso del otro técnico (404 en lectura, creación, aumento y retirada), Edge (401) y que las filas no cambian.
- Evidencia en [tests/M3.24.md](../../tests/M3.24.md).

## M3.25 — catálogo de componentes y tipos

Modo opt-in `--component-catalog-http <manifiesto-nuevo>`, en un destino vacío.

- Admin real (servicio de staff), técnico y HomeOwner.
- Verifica:
  - escritura 403 para técnico y HomeOwner, lectura 403 para HomeOwner y Edge 401;
  - el Admin crea entradas (un duplicado da 409);
  - el técnico lee en su ruta y suma stock real en su inventario (4 + 2 = 6);
  - borrar un tipo en uso da 409.
- Evidencia en [tests/M3.25.md](../../tests/M3.25.md).

## M3.26 — catálogo de servicios del técnico

Modo opt-in `--service-catalog-http <manifiesto-nuevo>`, en un destino vacío.

- Dos técnicos reales y servicios de catálogo reales.
- Dobles: Monitoring devuelve 0 servicios activos o en curso y Subscriptions está bloqueada.
- Verifica que el titular, vía `me`, crea, lee, desactiva y reactiva, y que el otro técnico recibe 404 y Edge 401, sin cambios en las filas.
- Evidencia en [tests/M3.26.md](../../tests/M3.26.md).

## M3.28 — inicio de sesión de cuentas no activas

Modo opt-in `--inactive-sign-in-http <manifiesto-nuevo>`, en un destino vacío.

- Usuario real; suspensión y reactivación con el `IUserCommandService` real.
- Verifica:
  - token previo 403 tras la suspensión;
  - inicio de sesión 403 sin token;
  - contraseña incorrecta 401;
  - inicio de sesión correcto tras reactivar.
- Evidencia en [tests/M3.28.md](../../tests/M3.28.md).

## M3.29 — cliente de solicitudes resuelto en el servidor

Modo opt-in `--service-request-client-http <manifiesto-nuevo>`, en un destino vacío.

- Dos HomeOwner reales y servicios de solicitudes reales.
- Dobles: elegibilidad de Subscriptions sintética; matching y snapshot bloqueados.
- Verifica:
  - el titular crea y lee su solicitud;
  - el otro HomeOwner recibe 404;
  - tras suspender el perfil del titular, su token previo recibe 403;
  - Edge recibe 401.
- Evidencia en [tests/M3.29.md](../../tests/M3.29.md).

## M3.30 — sugerencias de servicio

Modo opt-in `--service-suggestion-http <manifiesto-nuevo>`, en un destino vacío.

- Dos HomeOwner reales; sugerencias preparadas con el repositorio real.
- Verifica:
  - el titular lista, marca como vista y acepta (la solicitud se crea con su cliente; repetir da 409);
  - el otro HomeOwner recibe 404;
  - el token previo de un perfil suspendido recibe 403;
  - Edge recibe 401.
- Evidencia en [tests/M3.30.md](../../tests/M3.30.md).

## M3.31 — perfil de suscripción resuelto en el servidor

Modo opt-in `--subscription-profile-http <manifiesto-nuevo>`, en un destino vacío.

- Dos HomeOwner reales; suscripciones preparadas con el repositorio real; comandos de pago bloqueados.
- Verifica:
  - cada titular lee la suya;
  - un perfil suspendido por pago sigue leyendo estado, elegibilidad e historial;
  - un perfil desactivado recibe 403;
  - Edge recibe 401.
- Evidencia en [tests/M3.31.md](../../tests/M3.31.md).
