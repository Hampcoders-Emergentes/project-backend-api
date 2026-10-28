# M0.1 — Caracterización de la ingesta

Esta suite caracteriza el comportamiento actual de `DeviceReadingStream` y su contrato con `Reading`. No cambia la lógica de negocio ni acredita todavía idempotencia persistente, aislamiento entre locales o sincronización Edge/Cloud.

## Ejecutar

Requiere el SDK .NET 9, conforme a `TargetFramework` de la aplicación. Desde la raíz del repositorio:

```bash
dotnet build Hampcoders.Electrolink.API.csproj
dotnet test tests/ElectroLink.Tests.sln
```

En Rider, abre `tests/ElectroLink.Tests.sln`. Incluye la aplicación y el proyecto de pruebas; los casos se pueden ejecutar desde la ventana de Unit Tests. La solución está dentro de `tests/` para conservar la selección del único proyecto de aplicación al ejecutar `dotnet build` desde la raíz.

Para conservar resultados fuera de los archivos versionados:

```bash
dotnet test tests/ElectroLink.Tests.sln \
  --logger 'trx;LogFileName=m01.trx' \
  --results-directory tests/Electrolink.Backend.Characterization.Tests/bin/TestResults
```

Las versiones de xUnit, del adaptador y de Microsoft.NET.Test.Sdk provienen de la plantilla xUnit del SDK 9.0.319 y están fijadas en el proyecto. No se actualizan paquetes de la aplicación. El manifiesto Web excluye `tests/**` de sus elementos por defecto para impedir que las fuentes, dependencias y salidas de tests se compilen o publiquen como parte de la API.

## Cobertura delimitada

Se esperan **6 casos**, sin omitidos:

- Ingesta válida para cada origen existente: `EdgeDevice`, `EdgeBufferReplay` y `EDGE`. Conserva IDs, magnitudes, instante de captura y origen; publica un solo evento con las referencias correctas.
- Reintento deserializado en una instancia distinta con el mismo ReadingId: rechaza el duplicado y conserva lectura y evento originales.
- Otra identidad de lectura: se admite y produce su propio evento sin reemplazar la lectura anterior.
- Stream pausado: rechaza una lectura nueva y conserva estado, historial, eventos y última recepción.

Se utilizan únicamente objetos y datos sintéticos en memoria. Los tests no construyen el host ASP.NET, no ejecutan `Program.cs` o `EnsureCreated`, no necesitan secretos ni usan PostgreSQL, MQTT, proveedores o relés. La restauración inicial de paquetes sí necesita acceso a NuGet. No ejecutar la API para correr estos tests.

La cobertura no demuestra concurrencia, retención fuera de la ventana de 120 lecturas, comportamiento del worker MQTT, pertenencia por local, entrega de alertas ni el flujo `complete-homeowner` de AGENTS.md. Esos escenarios requieren tareas y pruebas adicionales.

## Compatibilidad y recuperación

No se alteran contratos REST, modelos de dominio, tablas ni datos. Si se retira esta tarea, revisar el parche y quitar solo sus archivos nuevos y la exclusión `tests/**`, preservando cualquier trabajo posterior del usuario. No requiere migraciones ni operaciones destructivas de Git.

Referencias oficiales consultadas: [pruebas con xUnit](https://learn.microsoft.com/dotnet/core/testing/unit-testing-csharp-with-xunit) y [elementos implícitos del SDK](https://learn.microsoft.com/dotnet/core/project-sdk/overview). Su contenido se consultó en el repositorio oficial `dotnet/docs`.
