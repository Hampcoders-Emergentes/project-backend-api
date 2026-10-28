# Mapa de Código - ElectroLink Backend

## Estructura General

Monolito modular con arquitectura DDD. Proyecto: `Hampcoders.Electrolink.API.csproj` (.NET 9)

## Bounded Contexts

| Contexto | Estado | Responsabilidad |
|---|---|---|
| **IAM** | Existente/Modificado | Identidad, autenticación, autorización. JWT (sub=userId). Middleware propio |
| **Profiles** | Existente | Perfiles de usuario (HomeOwner/Company). Complete-homeowner flow |
| **Assets** | Existente/Modificado | Dispositivos IoT, activos, retirada lógica (M3.6) |
| **StoreManagement** | **Activo/Ampliado (M3.9)** | Locales/áreas/equipos, telemetría, historial mediciones. Tiene su propio DbContext |
| **AlertManagement** | Existente | Alertas, ACK/RECOGNIZED, puente con StoreManagement |
| **Processing** | Existente/Modificado | Ingesta Edge, streams, anomalías, relés |
| **Monitoring** | Existente | ServiceExecution (operaciones servicios técnicos - NO es Electrical Monitoring) |
| **Planning** | Existente | Mantenimiento, solicitudes |
| **Subscriptions** | Existente | Suscripciones/pagos (Stripe) |
| **Analytics** | Existente | Reportes/proyecciones |
| **Shared** | Existente | Kernel compartido, AppDbContext, Outbox, Mediator/Cortex |

## Persistencia

- **AppDbContext** (`Shared/Infrastructure/Persistence/EFC/Configuration/Extensions/AppDbContext.cs`): Módulos heredados
- **StoreManagementDbContext**: Capacidades nuevas StoreManagement, integra AlertManagement como puente transaccional
- **PostgreSQL** con Npgsql 9.0.4, PostGIS habilitado
- **Inicio:** `context.Database.EnsureCreated()` (ver C-04 en context.md)
- **Outbox:** `Shared/Infrastructure/BackgroundServices/OutboxProcessorBackgroundService.cs` (polling 5s)

## Autenticación/Autorización

- **Middleware:** `IAM/Infrastructure/Pipeline/Middleware/Components/RequestAuthorizationMiddleware.cs`
- **JWT:** sub = user ID (us-...), ProfileId obtenido vía GetProfileClaimsAsync
- **Edge:** Soporte con EdgeApiKey (atributos AllowEdgeApiKey/RequireEdgeApiKey)
- **Autorización por recurso:** Verificación titularidad + ascendencia

## Endpoints Clave

**StoreManagement (M3.9):**
- `GET /api/v1/stores/{storeId}/areas/{areaId}/equipment/{equipmentId}/measurements` - Historial paginado
  - Params: from, to (ISO con zona), page, pageSize
  - Auth: Bearer JWT (humano)
  - Acceso: Titular del store + ascendencia verificada
  - Retorno: {items, page, pageSize, hasMore}
  - Disponible tras retirada dispositivo

**Otros relevantes:**
- POST `/api/v1/profiles/complete-homeowner` (ver AGENTS.md)
- Endpoints de ingesta Edge en Processing
- Endpoints Assets para dispositivos

## Archivos Críticos M3.9

### Producción
- `StoreManagement/Application/MeasurementHistoryQuery.cs` (nuevo)
- `StoreManagement/Application/EquipmentTelemetryService.cs`
- `StoreManagement/Domain/Repositories/IEquipmentTelemetryRepository.cs`
- `StoreManagement/Infrastructure/Persistence/EquipmentTelemetryRepository.cs`
- `StoreManagement/Interfaces/REST/EquipmentDeviceController.cs`

### Pruebas
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/MeasurementHistoryHttpTests.cs` (nuevo)
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/M39MeasurementHistoryDemo.cs` (nuevo)
- `tests/Electrolink.Backend.Characterization.Tests/StoreManagement/EquipmentTelemetryScenario.cs`

### Herramientas
- `tools/StoreManagementPostgresDemo/*` - Ensayos PostgreSQL reales (modo --measurement-history)
- `tests/M3.9.md` - Informe completo

## Dependencias Notables

- MediatR, Cortex
- FluentValidation (presente)
- Stripe.net, CloudinaryDotNet, OpenAI
- MQTTnet
- Npgsql.EntityFrameworkCore.PostgreSQL, NetTopologySuite
- Serilog (configurado)

## Notas Importantes

- **Monitoring vs Processing:** No confundir `Monitoring/` (ServiceExecution) con telemetría IoT (está en `Processing/`)
- **AppDbContext vs StoreManagementDbContext:** Usar el correcto según contexto
- **Sin DDL M3.9:** Solo código, usa schema existente
- **Compatibilidad:** Contrato aditivo, no altera rutas/DTO previos
