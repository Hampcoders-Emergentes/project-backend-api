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
