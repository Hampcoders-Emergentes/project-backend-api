using Electrolink.Backend.Characterization.Tests.StoreManagement;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: StoreManagementPostgresDemo [--installation-http <manifest> | --store-alert-history <manifest> | --alert-history <manifest> | --company-layers <manifest> | --company-http <manifest> | --measurement-history <manifest> | --device-read <manifest> | --retirement-http <manifest> | --revocation <manifest> | --real-backend <manifest> | --export-legacy-schema <output.sql> | <manifest>]");
    return 2;
}
try
{
    if (args.Length == 2 && args[0] == "--export-legacy-schema")
    {
        if (File.Exists(args[1])) throw new InvalidOperationException("Refusing to overwrite schema review file");
        await File.WriteAllTextAsync(args[1], M35PostgresDemo.LegacySchema());
        Console.WriteLine("Legacy schema exported offline for review; no database connection opened.");
    }
    else if (args.Length == 2 && args[0] == "--installation-http") await M313DeviceInstallationDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--store-alert-history") await M312StoreAlertHistoryDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--alert-history") await M311AlertHistoryDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--company-layers") await M310CompanyHttpDemo.Run(args[1], verifyLayers: true);
    else if (args.Length == 2 && args[0] == "--company-http") await M310CompanyHttpDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--measurement-history") await M39MeasurementHistoryDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--device-read") await M38DeviceReadDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--retirement-http") await M37RetirementHttpDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--revocation") await M36RevocationDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--real-backend") await M35PostgresDemo.Run(args[1]);
    else if (args.Length == 1) await M33PostgresDemo.Run(args[0]);
    else throw new ArgumentException("Unknown option");
    return 0;
}
catch (Exception ex)
{
    // Only assertion messages contain our known synthetic values; provider messages stay private.
    if (ex is Xunit.Sdk.XunitException) Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ex.StackTrace);
    Console.Error.WriteLine("FAIL PostgreSQL demo: " + ex.GetType().Name + ". PostgreSQL demonstration incomplete; preserve the disposable environment for diagnosis.");
    return 1;
}
