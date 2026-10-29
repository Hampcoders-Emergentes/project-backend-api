using Electrolink.Backend.Characterization.Tests.StoreManagement;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: StoreManagementPostgresDemo [--subscription-profile-http <manifest> | --service-suggestion-http <manifest> | --service-request-client-http <manifest> | --inactive-sign-in-http <manifest> | --service-catalog-http <manifest> | --component-catalog-http <manifest> | --inventory-ownership-http <manifest> | --portfolio-ownership-http <manifest> | --property-ownership-http <manifest> | --installation-property-http <manifest> | --registration-http <manifest> | --assignment-race <manifest> | --assignment-http <manifest> | --installation-http <manifest> | --store-alert-history <manifest> | --alert-history <manifest> | --company-layers <manifest> | --company-http <manifest> | --measurement-history <manifest> | --device-read <manifest> | --retirement-http <manifest> | --revocation <manifest> | --real-backend <manifest> | --export-legacy-schema <output.sql> | <manifest>]");
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
    else if (args.Length == 2 && args[0] == "--subscription-profile-http") await M331SubscriptionProfileDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--service-suggestion-http") await M330ServiceSuggestionDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--service-request-client-http") await M329ServiceRequestClientDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--inactive-sign-in-http") await M328InactiveSignInDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--service-catalog-http") await M326ServiceCatalogDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--component-catalog-http") await M325ComponentCatalogDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--inventory-ownership-http") await M324InventoryOwnershipDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--portfolio-ownership-http") await M323PortfolioOwnershipDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--property-ownership-http") await M322PropertyOwnershipDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--installation-property-http") await M319SubscriptionInstallationDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--registration-http") await M316DeviceRegistrationDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--assignment-race") await M315AssignmentRaceDemo.Run(args[1]);
    else if (args.Length == 2 && args[0] == "--assignment-http") await M314DeviceAssignmentDemo.Run(args[1]);
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
