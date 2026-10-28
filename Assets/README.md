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
