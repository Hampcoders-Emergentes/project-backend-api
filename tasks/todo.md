# M3.11

- [x] 1. Reproducir la brecha funcional con pruebas HTTP de intervalo/compatibilidad/acceso.
- [x] 2. Añadir from/to por CreatedAtUtc y filtrado SQL paginado; compartir parser UTC sin cambiar mediciones.
- [x] 3. Verificar PostgreSQL nuevo, focalizadas/build/suite, documentación y recuperación; detener recurso propio.


# M3.12

- [x] RED HTTP: ruta por local ausente (3 fallos reales).
- [x] Implementar consulta protegida reutilizando reglas y sin N+1.
- [x] PostgreSQL nuevo, build/focalizadas/suite fail-fast, informe y relevo; detener solo recurso propio.


# M3.13

- [x] Leer fuentes y seleccionar una sola operación; confirmar la política material faltante.
- [x] RED real HTTP y corrección mínima en Application/Planning ACL, sin extender permisos a otras operaciones.
- [x] Demostración PostgreSQL nueva con identidades/relaciones reales y efectos aislados; corregir únicamente fixtures fallidos.
- [x] Build/focalizadas/suite fail-fast, documentación/relevo y recuperación; detener solo recursos propios.


# M3.14 — punto 1, primer paso

- [x] Contrastar develop publicado, relevo/migration y confirmar política de asignación.
- [x] RED HTTP/puente antes de corregir; implementar autorización de aplicación y selección explícita.
- [x] PostgreSQL nuevo con asignación→instalación HTTP y stock restante intacto; detener recurso propio.
- [x] Build/focalizadas/suite, informe/relevo y recuperación selectiva.
- [ ] Punto 1 siguiente incremento: reserva atómica concurrente, todavía no implementada.
- [ ] Punto 1 posterior: política de registro HTTP y selección persistida en el workflow comercial.


# M3.15 — punto 1, segundo incremento

- [x] Baseline real 228/228 y preservar estado previo M3.14.
- [x] Reproducir doble asignación con dos contextos y técnicos autorizados en PostgreSQL nuevo.
- [x] Control optimista de estado existente y 409 sin reintento; comprobar rollback/evento único.
- [x] Build, focalizadas 29/29, suite 228/228 y compatibilidad asignación→instalación HTTP.
- [x] Detener solo recursos propios; documentar límites, relevo y recuperación selectiva.
- [ ] Punto 1 restante: política/alta HTTP autorizada y selección persistida comercial; no iniciar sin delimitar su siguiente corte.


# M3.16 — corrección de política e inventario propio

- [x] Conservar base 228/228 y el trabajo M3.14/M3.15.
- [x] Sustituir la propuesta Admin por la corrección del usuario: técnico con stock propio.
- [x] Probar primero fallos de permiso, stock ajeno/sin titular, contador y Location.
- [x] Persistir pertenencia del servidor y consulta paginada/conteos propios; sin backfill automático.
- [x] Registro/asignación/consulta/instalación y carreras PostgreSQL aislados; 263/263 y 64/64.
- [x] Detener recursos propios, documentar esquema, límites y recuperación; revisar cambios antes de publicar.
- [ ] Selección persistida y coordinación comercial de Planning/Subscriptions, fuera del corte de stock propio.
