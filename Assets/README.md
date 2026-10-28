# Assets — operaciones HTTP autorizadas

Este documento cubre baja lógica (M3.7), lectura individual (M3.8) e instalación lógica (M3.13), con la convención kebab-case vigente. Los demás endpoints de inventario siguen fuera de estos cortes.

## Política confirmada

Decisión explícita del usuario en M3.7: **titular del inventario**. Puede solicitar la baja el usuario IAM autenticado con perfil Active HomeOwner/Company cuyo sujeto es propietario de la Property asignada al dispositivo, activa y no archivada. El sujeto debe existir según los contratos Profiles. Se resuelve UserId → ProfileId/RoleSubjectId → ClientIdentity → Property.Owner; no se deduce del UserId ni de un Store.

No hay bypass Admin, técnico, operador o clave Edge. Un dispositivo sin asignación queda denegado. La titularidad de StoreManagement no otorga este permiso. No se crean roles o endpoints alternativos.

## Contrato

Cuerpo conservado: `{ "reason": "..." }`. El actor se obtiene de `HttpContext.Items["User"]`, cargado por IAM; campos adicionales enviados en el cuerpo no se utilizan para determinar identidad/propietario. La acción incorpora el filtro humano Authorize existente. Clave Edge sola no basta.

| Caso | HTTP | Efectos |
|---|---|---|
| Sin autenticación o solo clave Edge | 401 | Sin cambios ni evento de baja |
| Usuario sin pertenencia/perfil válido, propiedad no elegible, dispositivo sin asignación | 404 | Sin cambios ni evento de baja |
| ID inexistente | 404 | Mismo mensaje genérico, sin revelar existencia |
| Titular autorizado | 204 | Baja lógica y evento existente |
| Repetición por el mismo titular aún elegible | 204 | Sin otro evento ni cambios de estado persistidos |

La comprobación de pertenencia se realiza en **aplicación**, mediante DecommissionOwnedDeviceCommand con actor del servidor, no solo en el controller. Consulta repositorios existentes antes de guardar/publicar. No se exige Installed para repetir una baja ya realizada; sí se revalida la relación autorizante en cada petición. La operación delega en el ciclo de vida existente después de autorizar.

DecommissionDeviceCommand anterior permanece para operaciones técnicas internas explícitamente confiables y escenarios anteriores; no es la ruta HTTP. No se debe exponer ese comando sin actor desde otro endpoint. Su autorización HTTP no se extrapola a los demás servicios de inventario.

## Efectos y compatibilidad

Se mantiene Decommissioned/Disconnected, retirada del ID de la colección instalada de la Property y recálculo de monitoreo, sin borrar dispositivo/asignación/histórico. La ingesta nueva vinculada queda denegada por autoridad de inventario, mientras que el titular conserva la consulta histórica y ACK originales. No hay acción física, resolución o notificación nueva.

Cambio de seguridad intencional: antes cualquier usuario autenticado podía retirar un dispositivo conocido; ahora se deniega al ajeno. La ruta, método, cuerpo y 204 autorizado permanecen. El error de ID inexistente se mapea explícitamente a 404 en esta acción, en lugar de dejar escapar la excepción del servicio. Los demás endpoints y errores no se cambian.

Prueba PostgreSQL real con HomeOwner y dos usuarios: RED reprodujo 204 ajeno con mutación/evento; GREEN denegó antes de la baja válida y mantuvo todos los snapshots. Company se cubrió con prueba de aplicación y dobles sintéticos, no con una cuenta Company real. No se acredita concurrencia/propagación, autorización de otros endpoints o el host completo. No se añade auditoría persistida del actor de baja al evento/esquema original.

Evidencia, comandos, handlers excluidos y recuperación: [tests/M3.7.md](../tests/M3.7.md).


## Consulta individual — M3.8

Política de lectura confirmada por separado: `GET /api/v1/io-t-devices/{id}` exige usuario IAM con perfil Active HomeOwner/Company y sujeto activo, propietario de la Property asignada, activa y no archivada. Sin bypass Admin/Edge. Un retirado que conserva esa relación sigue visible; sin asignación se deniega. La identidad procede de autenticación, nunca del querystring.

La consulta nueva `GetOwnedIoTDeviceByIdQuery` aplica `InventoryDeviceAccess` en Application. La baja M3.7 reutiliza la misma elegibilidad, sin cambiar su comportamiento. No se aplica esta regla automáticamente a otros endpoints ni a los queries técnicos existentes. Ajeno/no disponible → 404 genérico; anónimo/solo Edge → 401; titular → 200 con los mismos ocho campos del DTO, sin claves ni hashes. Evidencia y límites: [tests/M3.8.md](../tests/M3.8.md).


## Instalación lógica — M3.13

Política confirmada por el usuario específicamente para esta escritura: solo el técnico IAM con perfil Active Technician asignado en una solicitud real de instalación compatible con el dispositivo y su propiedad activa/no archivada. Sin bypass Admin/Edge. La política de titular HomeOwner/Company de lectura/baja no concede permiso de instalación.

`PUT /api/v1/io-t-devices/{id}/install/{propertyId}` conserva cuerpo `{technicianId,firmwareVersion,installedAt}` y respuesta 200 con los ocho campos de IoTDeviceResource. Status conserva su enum numérico (Installed=2 en TestServer actual). El actor procede de IAM en servidor; technicianId debe coincidir con su sujeto Profiles real. Campos adicionales no determinan identidad o pertenencia.

DeviceInstallationService autoriza en Application: perfil activo/rol Technician/sujeto confirmado; dispositivo asignado a esa misma Property; propiedad activa/no archivada; InstallationRequestId del dispositivo resuelto en Planning. El lector ACL verifica categoría Installation, snapshot de instalación, cliente igual a Property.Owner, estado Assigned, técnico seleccionado y asignación persistida referida por la solicitud, con mismo RequestId/técnico y estado Assigned. No basta un ID textual o el rol técnico por sí solo. No exige ni modifica autoridad de ingesta.

| Caso | HTTP | Efectos |
|---|---|---|
| Anónimo/solo Edge | 401 | Sin instalación/eventos |
| Técnico sin asignación, perfil no elegible, técnico del cuerpo incompatible, solicitud/propiedad/dispositivo sin relación válida | 404 genérico | Sin guardado ni eventos |
| Técnico asignado y dispositivo Assigned | 200 | Instalación lógica persistida; evento DeviceInstalled existente |
| Repetición por técnico aún autorizado, dispositivo ya Installed | 409 | Sin otra instalación/evento |

Se conserva el comando técnico RecordDeviceInstallationCommand para handlers internos confiables; la acción HTTP usa el nuevo comando con actor y servicio autorizado antes de delegar. No se modifican el agregado, esquema, validadores o auditoría del evento. InstalledAt sigue siendo el valor del cuerpo; no acredita hora/actuación física verificada. La comprobación de relaciones no añade serialización frente a reasignaciones concurrentes.

El registro y la asignación del fixture continúan mediante comandos técnicos reales. Solicitud/asignación se preparan con dominio/repositorios/UoW; no se acredita todo el workflow de catálogo/matching, Subscriptions/PendingInstallation o aprovisionamiento HTTP completo. En la demo se ejecuta el handler Assets de logging y el observador de eventos existente; se excluyen Processing (activación de streams) y Subscriptions (activación y eventos posteriores), sin afirmar efectos del host completo. [Pruebas, límites y recuperación M3.13](../tests/M3.13.md).


## Asignación explícita — M3.14 (primer paso del aprovisionamiento)

Política de escritura aprobada por el usuario: técnico IAM/Profiles Active Technician asignado a una solicitud Installation válida, propiedad activa/no archivada correspondiente al cliente de la solicitud y dispositivo seleccionado explícitamente por ID del stock global, InStock y sin propiedad/solicitud previa. El stock libre actual no tiene titular; esta política se confirmó específicamente y no se dedujo de lectura, baja o instalación.

`POST /api/v1/io-t-devices/{id}/assign/{propertyId}` conserva cuerpo `{installationRequestId}` y respuesta 200 IoTDeviceResource de ocho campos (Assigned=1 numérico). El actor procede de IAM en servidor. DeviceAssignmentService valida perfil/sujeto, propiedad y la relación de solicitud/técnico con el lector ACL de Planning existente, antes de buscar/asignar stock. Sin bypass Admin/Edge ni selección por primer registro. Ajeno/relación no disponible → 404 genérico; anónimo/Edge → 401; stock ocupado o repetición secuencial autorizada → 409 sin nueva asignación/evento. Cuerpos inválidos conservan validación MVC.

La escritura delega en el comando técnico existente tras autorizar: conserva vínculo Property/InstallationRequestId, lista de dispositivos de la propiedad y evento DeviceAssignedToProperty. Assigned es estado lógico: no acredita instalación o comunicación; se conserva la semántica previa de AddInstalledDevice/HasActiveIoTMonitoring y no se presenta ese indicador como prueba física.

El puente IoTInstallationServiceScheduledEventHandler ahora exige SelectedDeviceId explícito; vacío/blanco arroja ArgumentException antes de escribir. Con ID, reenvía exactamente ese dispositivo al comando técnico interno. El evento sigue siendo una entrada interna confiable; esta comprobación de selección no acredita autorización IAM del evento. La fachada técnica antigua AssignAvailableDeviceToPropertyAsync permanece por compatibilidad, pero este handler ya no la invoca. El productor Planning aún emite SelectedDeviceId vacío: su origen/captura persistida sigue pendiente; no se rellena con inventario arbitrario. Los eventos sin selección no completan asignación, incluso con stock disponible.

**Punto 1 todavía abierto:** autorización del alta HTTP, captura de selección en el workflow comercial y reserva atómica del stock. Las verificaciones actuales antes de guardar no garantizan exclusión entre dos solicitudes simultáneas; no se acepta una doble asignación como regla de negocio ni se acredita uso productivo concurrente. [Evidencia y recuperación M3.14](../tests/M3.14.md).


## M3.15 — competencia por el mismo stock

El estado existente de IoTDevice es ahora un token de concurrencia EF: SaveChanges comprueba el estado originalmente leído al actualizar/eliminar el dispositivo. Dos asignaciones que leen InStock no pueden guardar ambas después de la primera transición a Assigned. El POST assign traduce la excepción de concurrencia que identifica ese dispositivo a su 409 existente, sin reintento o sobrescritura. El SaveChanges transaccional revierte también la lista de la propiedad perdedora y el comando publica eventos solamente después de guardar. Sin columna, DDL o permisos nuevos.

Demostrado con dos técnicos autorizados, solicitudes y propiedades diferentes: RED 200/200, dos membresías/eventos; GREEN 200/409, una membresía/evento y propiedad perdedora intacta. Se conserva asignación→instalación HTTP. Este token afecta a todos los guardados EF de IoTDevice; otros comandos técnicos pueden propagar DbUpdateConcurrencyException cuando compiten. No se cambian aquí sus contratos HTTP.

No resuelve escrituras externas que omitan EF, cambios sin transición de estado, concurrencia de dispositivos diferentes sobre la misma lista de propiedad, revocación/reasignación simultánea ni el intervalo entre guardar y publicar eventos. Punto 1 sigue pendiente de alta autorizada y selección persistida comercial. La advertencia de reserva del informe M3.14 queda atendida para **el mismo dispositivo** por este corte; no equivale a cerrar todo aprovisionamiento. [Evidencia M3.15](../tests/M3.15.md).


## M3.16 — política vigente: stock IoT propio del técnico

El usuario corrigió expresamente la propuesta de Admin/SuperAdmin: **el técnico gestiona y registra sus dispositivos en su inventario**. Esta decisión sustituye también el stock global HTTP de M3.14. No hay bypass por AccessRole administrativo: se requiere IAM activo, perfil Active Technician, subject técnico concordante con el usuario e inventario técnico existente. El cuerpo no elige titular; StockTechnicianId procede del servidor. ComponentStock/Reservations y sus cantidades no se reinterpretan como dispositivos IoT.

- `POST /api/v1/iot-devices`: registro propio, 201 con DTO anterior de ocho campos y Location consultable por el técnico. Alias POST `/api/v1/io-t-devices` conserva exactamente la misma protección. Clave hasheada, sin clave/hash en respuesta; serial único global, repetición/carrera →409 genérico; campos vacíos o límites de serial/firmware →400.
- `GET /api/v1/iot-devices/my-stock?page=1&pageSize=20`: listado propio paginado (máximo 100), items {id,serialNumber,firmwareVersion,status}, totalCount de dispositivos registrados por el técnico en todos los estados y availableCount de InStock sin propiedad/solicitud. No son cantidades de componentes ni una reserva comercial.
- `GET /api/v1/iot-devices/{id}`: lectura del dispositivo propio del técnico; ajeno/sin titular/desconocido →404. El GET anterior `/io-t-devices/{id}` conserva la política de titular del inventario cliente/propiedad de M3.8; no recibe bypass técnico o Admin.
- GET legado `/io-t-devices` y `/by-status/{status}` ahora filtran por técnico autenticado. `/active-count/{ownerId}` exige que ownerId sea su subject TechnicianId y cuenta solamente Installed/Maintenance propios mediante SQL; ID ajeno →404. Otros perfiles →403. El conteo interno anterior usado por otros contextos no se modifica ni se da por validado aquí.
- POST legado assign mantiene técnico asignado/solicitud/propiedad, estado libre y exclusión concurrente, y ahora exige StockTechnicianId igual a ese técnico. Stock ajeno o sin procedencia →404 sin mutaciones. Asignar reduce disponibilidad; instalar no devuelve el equipo al stock.

Persistencia aditiva: columna nullable stock_technician_id e índice, sin backfill automático. Dispositivos anteriores NULL permanecen intactos y fuera del stock personal/asignación HTTP; no se atribuyen por serial/nombre. El SQL 004 de Assets es **borrador para revisión**, no se ejecutó sobre bases existentes/compartidas. El modelo exportado para destinos nuevos ya incluye columna/índice. Desplegar contra un esquema anterior requiere antes un cambio de esquema autorizado; no se despliega en este corte.

263/263 y 64/64 focalizadas; PostgreSQL real verifica alta→consulta→asignación→instalación, contadores propios, serial concurrente y separación de técnicos; M3.15 se revalida con dos solicitudes del mismo titular. [Informe y recuperación M3.16](../tests/M3.16.md). La selección persistida de Planning/comercial, transferencias/conciliación de stock anterior y validación automática de todos los materiales de un servicio siguen pendientes; el disponible es una consulta informativa, no una garantía/reserva del servicio.
