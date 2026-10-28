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
