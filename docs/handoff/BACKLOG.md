# Backlog - ElectroLink Backend

## Prioridad Alta (Bloqueantes)

### M2.5 - Conciliación real
- **Estado:** BLOQUEADO (solo conciliación real)
- **Descripción:** Conciliación real de IDs entre sistemas. M2.5 bloquea únicamente la conciliación real, NO toda la migración.
- **Bloqueo:** Faltan exports sanitizados y autorizados
- **Acción:** Esperar exports autorizados, no inventar datos
- **Dependencias:** Ninguna

### Company HTTP real
- **Estado:** DEMOSTRADO dentro del alcance de M3.10; sin bloqueo por el 400 histórico.
- **Descripción:** --company-http ejecutado con dos usuarios Company, IAM/Profiles/Assets y PostgreSQL nuevos reales. complete/company 201, sujetos Active persistidos, GET titular 200/ajeno 404 sin mutaciones. Aparte de las 204 pruebas xUnit.
- **Acción:** 400 histórico: causa no recuperable con la evidencia disponible; reabrir si reaparece con captura por etapa. Company se da por demostrado dentro de M3.10. No ampliar su diagnóstico sin evidencia nueva.
- **Nota:** Estado ACKNOWLEDGED es el implementado en AlertManagement (ver M3.4). No confundir con un hipotético "RECOGNIZED".

## Prioridad Media

### Aprovisionamiento/Instalación inventario
- **Estado:** PENDIENTE
- **Descripción:** Preparación técnica por servicios existe, no acredita todos permisos HTTP
- **Acción:** Revisar permisos HTTP completos

### Resolución alertas, notificaciones, gestión incidentes
- **Estado:** PENDIENTE
- **Descripción:** Funcionalidades completas pendientes
- **Alcance:** Más allá de ACK actual

### Consumo energético y mantenimiento
- **Estado:** PENDIENTE - Contrastar con migration.md
- **Nota:** Historial M3.9 NO implementa kWh, costos, consumo energético

### Migración datos existentes y adopción esquemas
- **Estado:** PENDIENTE
- **Acción:** Definir estrategia cuando haya datos reales

### Integración consumidores vigentes, Edge físico, preparación productiva
- **Estado:** PENDIENTE
- **Nota:** M3.9 no acredita integración web/móvil/Edge real

## Defectos Conocidos

### Outbox - mensajes fallidos marcados como procesados
- **Estado:** Documentado, NO resuelto por M2.7 (solo corrigió cancelación)
- **Ubicación:** `Shared/Infrastructure/BackgroundServices/OutboxProcessorBackgroundService.cs`
- **Impacto:** Crítico si ruta depende del polling
- **Acción:** Revisar lógica catch que asigna ProcessedOnUtc

### Ingesta crea stream desconocido con propietario/local demo
- **Estado:** Documentado (C-06 en context.md)
- **Ubicación:** `Processing/Application/Internal/CommandServices/DeviceReadingStreamCommandService.cs`
- **Acción:** Separar demo/provisionamiento, corregir pertenencia

### DeviceReadingStreamCommandService descarta tarea evaluación
- **Estado:** Documentado (C-07)
- **Código:** `_ = anomalyService.Handle(...)`
- **Acción:** Revisar finalización, errores, alcance

## Propuestas NO Autorizadas (No Iniciar)

### M3.9b - Consulta individual medición por ReadingId
- **Propuesto en M3.9.md línea 132**
- **Descripción:** GET individual bajo misma jerarquía, reutilizar política
- **Requisito:** JUSTIFICAR consumidor y descartar contrato equivalente ANTES de implementar
- **Estado:** NO AUTORIZADO

## Tareas de Mantenimiento

- [ ] Actualizar fecha objetivo si se retoma después del 25 Oct 2026
- [ ] Revisar handlers excluidos antes afirmar integración host completo
- [ ] Actualizar .NET 9 (EOL 10 Nov 2026) - evaluar separado
- [ ] Revisar middleware Edge/anon - ver alcance rutas
- [ ] Validar CORS/issuer/audience JWT configuración

## Validación Pendiente

- [x] Build API M3.10: 0 errores, NU1603 previo
- [x] Suite final M3.10: 204/204, separada del modo Company PostgreSQL ejecutado
- [x] Rama main y HEAD 8b62b619 verificados; trabajo sin commit preservado
- [ ] Comparar con snapshots context.md/migration.md
- [ ] Revisar archivos sin commit vs M3.9


### Precisión del diagnóstico Company

El filtro no conserva el cuerpo del incidente antiguo. El TRX encontrado solo confirma 400 frente a 201 y apunta a la aserción de sign-up en la fuente preservada. No atribuirlo a complete/company ni a un campo concreto sin evidencia adicional. No seguir solicitando un archivo de payload que el usuario aclaró que no existe.

--company-layers verificó las capas del recorrido actual, rechazos controlados y unicidad 409. Ante una futura recurrencia, el helper ya captura etapa/Content-Type/error sanitizado. No bloquea el recorrido Company actualmente demostrado; no certifica todas las operaciones de Profiles. Ver tests/M3.10-layers.md.


## Corte activo M3.11

Filtro temporal del historial de alertas por equipo, trazado a US19 y migration.md M5/M6. AppleDouble/CS2015 sigue siendo una limitación posible del build ordinario: usar solo la exclusión CLI documentada, no afirmar build limpio sin condiciones. Mapeos TaxId de DomainExceptionFilter son trabajo del usuario preservado.


### M3.11 — filtro temporal de alertas: IMPLEMENTADO

Backend de US19: ruta por equipo con periodo de creación UTC, status/severity y página. 207/207 y modo PostgreSQL relevante pasado, sin DDL. No completa el escenario offline o todos los aspectos de US19. Company M3.10 queda demostrado; 400 histórico no bloqueante y sin causa recuperable con lo disponible. Ver tests/M3.11.md.


## M3.12 — historial por local: IMPLEMENTADO

US19: GET /api/v1/stores/{storeId}/alerts con equipo opcional, mismos filtros/DTO/página; sin duplicar reglas ni abrir resolución/notificaciones. 210/210 y focalizadas 18/18; modo PostgreSQL relevante pasado. Recurso propio detenido, datos retenidos, sin commits. Evidencia y límites en tests/M3.12.md.

Siguiente tarea pequeña propuesta, **sin iniciar**: contrastar el aprovisionamiento HTTP del inventario con migration.md y consumidores vigentes, y seleccionar una única brecha de autorización/operación demostrable con fixtures aislados. La preparación técnica por servicios no acredita esos permisos. No ampliar incidentes o hardware por inferencia. Continúan pendientes M2.5 real, integración de clientes/cache offline y operación productiva.


## M3.13 — instalación lógica HTTP: IMPLEMENTADO

Se cerró una sola operación: técnico IAM/Profiles activo asignado a solicitud Installation, propiedad y dispositivo compatibles. 218/218 y focalizadas 19/19; demostración PostgreSQL relevante aprobada. Registro/asignación siguen siendo preparación técnica y sus permisos HTTP permanecen pendientes; no extender automáticamente la política de instalación o la de lectura/baja.

Pendientes materiales: integración del recorrido comercial IoT PendingInstallation con propiedad/cliente/dispositivo y scheduling, adopción de inventario real/M2.5, carreras de reasignación frente a instalación, clientes y hardware. No se cambió el factory ni se activó Subscriptions/Processing para completar el ensayo.

Propuesta pequeña siguiente, **sin iniciar**: caracterizar la selección de dispositivo y relación de solicitud en el puente de asignación existente antes de cerrar su HTTP. El handler ServiceAutomaticallyAssignedForIoTEventHandler publica SelectedDeviceId vacío; determinar su fuente autorizada y política antes de corregir, sin asignar un dispositivo por defecto.


## M3.14 — asignación HTTP: primer incremento implementado

POST assign ahora exige técnico activo asignado a solicitud Installation/propiedad del cliente y dispositivo InStock explícito sin asignación previa. El puente rechaza selección vacía y no usa primer stock implícito. 228/228, focalizadas 29/29, modo PostgreSQL nuevo demuestra asignación→instalación HTTP; stock adicional permanece libre. Sin commit/push de este corte.

**No cerrar el punto 1 completo.** Prioridad siguiente: hacer atómica la reserva del dispositivo ante peticiones simultáneas; la comprobación previa a guardar aún tiene ventana de carrera. Después: política y autorización del alta HTTP y captura/fuente persistida de SelectedDeviceId en el flujo comercial. Planning todavía publica ID vacío y se deniega asignación; no habilitar inventario por defecto para completar el circuito. M2.5 continúa pendiente solo para datos reales.


## Continuidad vigente M3.15 — reserva del mismo dispositivo

Se reprodujo la carrera con dos POST autorizados leyendo InStock: 200/200, dos propiedades/eventos. Corregida con Status como token de concurrencia EF y 409 específico en assign; GREEN 200/409, una propiedad/evento, rollback de la perdedora. DDL offline idéntico; misma política, ruta y DTO. Focalizadas 29/29, suite 228/228, más una regresión PostgreSQL concurrente opt-in y compatibilidad asignación→instalación aprobadas. Tres destinos nuevos propios detenidos con datos retenidos. Build 0 errores con exclusión AppleDouble; NU1603 previo. Los 24 archivos previos con Fact/Theory están intactos. Sin stage/commit/push; develop @42b9fb8 y M3.14 preservados.

La pendiente de reserva en M3.14 queda atendida para competencia por **el mismo dispositivo**; punto 1 continúa abierto. Próximo incremento propuesto: definir política propia y proteger el registro HTTP de stock, sin heredar permisos de asignación/instalación. Después, selección persistida en workflow comercial; Planning sigue produciendo selección vacía y se deniega sin elegir stock implícito. También quedan carreras entre dispositivos distintos sobre una misma propiedad y frente a revocación/reasignación, no acreditadas aquí. Otros escritores EF de IoTDevice pueden ahora observar excepciones de concurrencia; no se cambia su manejo en este corte. No uso real, clientes o hardware acreditados. Ver tests/M3.15.md para archivos, comandos y recuperación exclusiva; M2.5 solo bloquea conciliación real.


## Continuidad vigente M3.16 — stock del técnico

Corrección expresa del usuario: el registro pertenece al técnico que gestiona su inventario; se descarta Admin/SuperAdmin como permiso de registro y se sustituye la selección de stock global de M3.14 por stock propio. IAM activo + Active Technician, subject concordante e inventario existente; actor del servidor, sin titular enviado por cuerpo. POST canonical /api/v1/iot-devices y alias anterior igualmente protegidos; GET my-stock paginado con cantidades total/disponible, GET propio por ID/Location, listados y contador HTTP legados aislados. Assign rechaza stock ajeno o sin titular y conserva relación de solicitud/propiedad y token concurrente.

Columna nueva nullable stock_technician_id e índice; sin atribuciones automáticas del stock previo. Borrador SQL 004 de Assets para revisión, **no aplicado a bases compartidas/existentes**. Destinos nuevos usan export actual cd69a8a040494b4aaa17f1c2eff4af9e922dd08d3438fd93588469384b66de6f. El código requiere esquema actualizado antes de un despliegue futuro autorizado; no se despliega aquí.

Verificación final 263/263 y 64/64 focalizadas, build 0 errores con exclusión AppleDouble. PostgreSQL real: registro propio→consulta→asignación→instalación, stock disponible 2→1, contadores activos 1/0, serial 201/409 y exclusión de asignación 200/409. Cinco destinos propios de la sesión detenidos/retienen datos; el primero fue un prototipo Admin descartado y no acredita la política vigente. Fixture M3.15 ahora usa dos solicitudes del mismo titular; DeviceAssignmentHttpTests actualiza stock válido a técnico explícito y agrega dos rechazos. No se debilitan las aserciones concurrentes; otras 23 fuentes previas Fact/Theory intactas.

El usuario autorizó commit/push de los cambios al terminar: publicación de M3.14/M3.15/M3.16 en develop de Hampcoders-Emergentes/project-backend-api, base anterior 42b9fb8. Consultar git log -1 develop y origen para el hash de la publicación; los informes anteriores conservan su estado histórico sin commit. Mantener metadatos Rider/AppleDouble, manifiestos y binarios fuera. No supone permiso de despliegue o cambios compartidos.

Punto 1 sigue pendiente de selección persistida y coordinación comercial en Planning/Subscriptions, transferencias y conciliación autorizada del stock sin titular. La consulta disponible permite revisar dispositivos, pero no valida automáticamente todos los materiales, cupos o requisitos del servicio ni reserva al consultar. No activar primer stock implícito para completar scheduling. M2.5 continúa pendiente solo para conciliación real. Pruebas, archivos y recuperación selectiva en tests/M3.16.md.
