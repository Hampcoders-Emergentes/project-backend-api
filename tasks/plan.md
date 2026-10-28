# M3.11 — intervalo temporal del historial de alertas

Corte único: completar el filtro por fechas de US19, migration.md M5/M6 y §9.2. Ruta existente de alertas del equipo, mismos permisos/DTO/orden; from/to opcionales por CreatedAtUtc, UTC [from,to), máximo técnico 31 días como el historial de mediciones. Ambos ausentes conservan el comportamiento anterior.

1. Regresión HTTP: límites, offsets, composición con status/severity, compatibilidad sin fechas, acceso/ascendencia y entradas inválidas.
2. Implementar filtro EF antes de paginar; compartir solo el parser de intervalo UTC ya existente con mediciones, sin cambiar sus reglas. Sin DDL: índice EquipmentId/CreatedAtUtc ya existe.
3. Demostrar persistencia real desde scopes nuevos y cero escrituras; focalizadas, build, suite completa, documentación/relevo.

Dependencias satisfechas: jerarquía y autorización, alertas persistidas, repositorios EF y auxiliar PostgreSQL nuevos. No se implementan offline, resolución, notificaciones o detalles por ReadingId.

Archivos previstos: AlertService, IAlertRepository/AlertRepository, EquipmentAlertsController; helper UTC puro compartido y consumidor MeasurementHistoryQuery; doble y pruebas HTTP, ejecutor opt-in, README/informe/relevo.

Recuperación: snapshot y backups en /private/tmp/electrolink-m311-*, parche selectivo; no reset/clean ni restauración de Rider o cambios ajenos. Contenedor propio detenido sin eliminación.

Verificación: RED antes del filtro; GREEN focalizado; PostgreSQL nuevo; build/suite final fail-fast. Exclusión temporal CLI AppleDouble, verificada por comparación de Compile items: no excluye código/pruebas legítimos.


Cierre: implementado y verificado el 25 de octubre. RED 3, focalizadas 9/9, suite 207/207; PostgreSQL --alert-history pasado, detenido y conservado. Informe tests/M3.11.md. No se inició otra capacidad.


# M3.12 — historial de alertas por local

GET /api/v1/stores/{storeId}/alerts; equipmentId opcional; mismo AlertResource con EquipmentId. Reutilizar reglas UTC/status/severity/página de M3.11. Autorización IAM titular y ascendencia también en EF; equipo incompatible 404 sin ampliar filtro. Sin DDL salvo necesidad demostrada, sin autoridad de ingesta.

Archivos: controller/servicio/repositorio AlertManagement, lookup StoreManagement, dobles/pruebas HTTP, demo PostgreSQL opt-in y documentación. Aceptación: dos equipos y aislamiento entre locales/actores, filtros antes de paginar, orden estable, errores/vacío, sin escrituras, regresiones intactas. Recuperación: snapshot/backups/parche exclusivo /private/tmp/electrolink-m312-*.


# M3.13 — instalación lógica autorizada

Operación única existente: PUT /api/v1/io-t-devices/{id}/install/{propertyId}. Política Technician activo/asignado confirmada por el usuario; no hereda lectura/baja. Reutilizar comando técnico tras autorización de aplicación y lector ACL de relación Planning. Sin esquema/framework/proveedores nuevos. Aceptación: éxito persistido por HTTP, segundo técnico sin relación, anónimo/Edge, suplantación/recursos incompatibles denegados sin efectos, scope nuevo, DTO y comandos internos conservados.

Completado: RED 7/1; 19/19 focalizadas y 218/218 suite; PostgreSQL real aprobado con precondiciones técnicas explícitas; recursos propios detenidos con datos retenidos. Informe tests/M3.13.md. Recuperación exclusiva /private/tmp/electrolink-m313.patch y backups/snapshot, preservando cambios anteriores. No se inicia otra capacidad.


# M3.14 — primer incremento del punto 1

Asignación HTTP protegida y elección explícita de stock, con política material confirmada. Reutilizar identidad Profiles/lector Planning y comandos técnicos tras autorizar. Cambiar solo el puente para exigir selección explícita; no inventar origen persistido o permisos de registro. Aceptación: éxito HTTP/persistencia, ajeno/anónimo/Edge/incompatibles sin efectos, stock secundario intacto, continuidad con instalación M3.13 y contratos conservados.

Completado: base 218/218, RED 9/1, focalizadas 29/29, suite 228/228 y PostgreSQL nuevo pasado/detenido. Sin commits/push. Snapshot/backups/parche exclusivo en /private/tmp/electrolink-m314-*. Punto 1 sigue abierto; siguiente prioridad, reserva atómica de stock antes de acreditar concurrencia; luego alta autorizada y selección persistida.


# M3.15 — punto 1: reserva concurrente del mismo stock

Incremento acotado desde M3.14: reproducir dos lecturas InStock mediante interceptor exclusivo de pruebas y dos POST autorizados; reutilizar estado como token de concurrencia sin DDL; 409 para el perdedor, rollback de propiedad y un evento. Completado: RED real 200/200 → GREEN 200/409, focalizadas 29/29, suite 228/228 y compatibilidad PostgreSQL asignación→instalación aprobadas. Recursos propios detenidos. Tests/M3.15.md conserva comandos y recuperación. Alta autorizada y selección persistida no se implementan ni se dan por aprobadas.


# M3.16 — stock propio del técnico y publicación autorizada

Política corregida: registrar/consultar dispositivos del técnico IAM/Profiles activo con inventario existente; propietario derivado del servidor, stock previo sin titular excluido sin backfill. Incorporar columna/índice aditivos únicamente en destinos locales nuevos; borrador para revisión futura, sin cambios compartidos. Consulta paginada y cantidades, listados/contador HTTP aislados, asignación solo de stock propio y Location consultable. No alterar cantidades ComponentStock o introducir transferencias/matching automático.

Completado y verificado: 263/263, focalizadas 64/64; PostgreSQL registro→consulta→asignación→instalación, carreras de serial y de asignación, recursos propios detenidos. Preparar commit/push con cambios M3.14/M3.15/M3.16 a develop según petición explícita, excluyendo metadatos/secretos/binarios. tests/M3.16.md registra límites, esquema requerido y recuperación. Selección persistida comercial no se da por terminada.
