# StoreManagement persistence

`StoreManagementDbContext` owns the `store_management` schema (stores, areas, equipment with their thresholds,
device bindings, measurements) and the `alert_management` schema (alerts).

The schema is versioned with EF Core migrations in `Migrations/`; the application context has its own set under
`Shared/Infrastructure/Persistence/EFC/Migrations`. `Program.cs` applies both at startup unless
`Database:MigrateOnStartup=false`.

```bash
# from the repository root (tool pinned in dotnet-tools.json)
dotnet tool restore
dotnet ef migrations add <Name> --context StoreManagementDbContext --output-dir StoreManagement/Infrastructure/Persistence/Migrations
dotnet ef migrations add <Name> --context AppDbContext --output-dir Shared/Infrastructure/Persistence/EFC/Migrations
# apply manually: set ELECTROLINK_DESIGN_CONNECTION, then
dotnet ef database update --context StoreManagementDbContext
```

Design-time factories (`*ContextFactory.cs`) let `dotnet ef` run without starting the host or a database.
