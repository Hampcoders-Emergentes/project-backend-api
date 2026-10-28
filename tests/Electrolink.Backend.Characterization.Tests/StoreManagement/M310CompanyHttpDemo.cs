using System.Net;
using System.Net.Http.Json;
using Hampcoders.Electrolink.API.Assets.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Assets.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Assets.Domain.Repositories;
using Hampcoders.Electrolink.API.Assets.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public static class M310CompanyHttpDemo
{
    public static async Task Run(string manifestPath, bool verifyLayers = false)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath)); var m = json.RootElement;
        string Read(string key) => m.GetProperty(key).GetString()!;
        var task = Read("taskId"); var db = Read("database"); var host = Read("host"); var port = m.GetProperty("port").GetInt32();
        if (!Regex.IsMatch(task, "^[0-9a-f]{32}$") || db != "electrolink_m33_" + task || host != "127.0.0.1" || port is < 1024 or > 65535)
            throw new InvalidOperationException("Unrecognized disposable destination");
        var connection = new NpgsqlConnectionStringBuilder { Host = host, Port = port, Database = db, Username = "postgres", Password = Read("password"),
            Pooling = false, Timeout = 5, CommandTimeout = 30, IncludeErrorDetail = false }.ConnectionString;
        await using (var pg = new NpgsqlConnection(connection))
        {
            await pg.OpenAsync(); await using var command = new NpgsqlCommand("SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()", pg);
            Assert.Equal("electrolink-m33:" + task, await command.ExecuteScalarAsync());
        }
        var trace = verifyLayers ? new CompanyCompletionTrace() : null;
        using var h = new RealBackendScenario(connection, Read("jwtSecret"), Read("edgeKey"), "SYNTHETIC-M310-" + task, includeRetirementEndpoint: true, observeAssetMutations: true, companyTrace: trace);
        using (var empty = h.Scope()) Assert.Equal(0, await empty.ServiceProvider.GetRequiredService<AppDbContext>().Profiles.CountAsync());
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "aA1!";
        async Task Observe(string path, HttpResponseMessage response)
        {
            Console.WriteLine("OBSERVED Company flow POST " + path + ": HTTP " + (int)response.StatusCode + "; Content-Type=" + response.Content.Headers.ContentType?.ToString());
            if (!response.IsSuccessStatusCode)
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var p in error.RootElement.EnumerateObject().Where(p => p.Name is "status" or "title" or "errors" or "message"))
                {
                    var safe = p.Value.GetRawText().Replace(password, "[REDACTED]", StringComparison.Ordinal)
                        .Replace(Read("jwtSecret"), "[REDACTED]", StringComparison.Ordinal).Replace(Read("edgeKey"), "[REDACTED]", StringComparison.Ordinal);
                    safe = Regex.Replace(safe, @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED JWT]");
                    Console.WriteLine("ERROR " + p.Name + ": " + safe);
                }
            }
        }
        h.ObserveCompanySignUp = response => Observe("/api/v1/authentication/sign-up", response);
        h.ObserveCompanyCompletion = response => Observe("/api/v1/profiles/me/complete/company", response);
        var fields = verifyLayers ? CompanyLayerChecks.Resource("M310-" + task) : null;
        await h.RegisterAndCompleteCompany("m310-owner-" + task + "@example.invalid", password, "M310-" + task,
            companyResource: fields, beforeCompletion: verifyLayers ? () => CompanyLayerChecks.Rejections(h, trace!, fields!) : null);
        await h.RegisterAndCompleteCompany("m310-other-" + task + "@example.invalid", password, "OTHER-M310-" + task, other: true,
            beforeCompletion: verifyLayers ? () => CompanyLayerChecks.Duplicate(h, trace!, fields!) : null);
        if (verifyLayers) await CompanyLayerChecks.Persisted(h, trace!, fields!);
        string deviceId;
        using (var persisted = h.Scope())
        {
            var sp = persisted.ServiceProvider; var facade = sp.GetRequiredService<IProfilesContextFacade>();
            var ctx = sp.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await ctx.Profiles.CountAsync()); Assert.Equal(2, await ctx.Set<Hampcoders.Electrolink.API.Profiles.Domain.Model.Entities.Company>().CountAsync());
            foreach (var user in new[] { h.Owner, h.Other })
            {
                var claims = await facade.GetProfileClaimsAsync(user.Id.Value);
                Assert.True(claims.HasValue); Assert.Equal("Active", claims.Value.ProfileStatus); Assert.Equal("Company", claims.Value.BusinessRole);
                Assert.NotNull(claims.Value.RoleSubjectId); Assert.StartsWith("comp-", claims.Value.RoleSubjectId);
                Assert.True(await facade.IsCompanyActiveAsync(claims.Value.RoleSubjectId!));
                Assert.NotNull(await sp.GetRequiredService<IPropertyPortfolioRepository>().FindByOwnerAsync(ClientIdentity.FromCompany(claims.Value.RoleSubjectId!)));
            }
            var owner = (await facade.GetProfileClaimsAsync(h.Owner.Id.Value))!.Value;
            var other = (await facade.GetProfileClaimsAsync(h.Other.Id.Value))!.Value;
            Assert.NotEqual(owner.ProfileId, other.ProfileId); Assert.NotEqual(owner.RoleSubjectId, other.RoleSubjectId);
            var property = await sp.GetRequiredService<IPropertyCommandService>().Handle(new CreatePropertyCommand(ClientIdentity.FromCompany(owner.RoleSubjectId!),
                Address.Create("Synthetic", "1", "Synthetic", "Synthetic", "Synthetic", "00000"), Geolocation.Create(0, 0, null, "SYNTHETIC"), EPropertyType.Commercial));
            Assert.NotNull(property);
            await sp.GetRequiredService<IPropertyPortfolioCommandService>().Handle(new AddPropertyToPortfolioCommand(
                ClientIdentity.FromCompany(owner.RoleSubjectId!), property.Id, "Synthetic", true, "OwnerOccupied"));
            var commands = sp.GetRequiredService<IIoTDeviceCommandService>();
            deviceId = (await commands.Handle(new RegisterIoTDeviceCommand("SYNTHETIC-M310-" + task,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), "synthetic-m310"))).Id.Value;
            await commands.Handle(new AssignDeviceToPropertyCommand(deviceId, property.Id.Value, "synthetic-installation"));
            await commands.Handle(new RecordDeviceInstallationCommand(deviceId, property.Id.Value, "tech-m310-synthetic-reference", null, "synthetic-m310", DateTime.UtcNow));
        }
        Console.WriteLine("PASS M3.10 real Company sign-up/completion/login: two Active profiles and Company subjects persisted in new scope; real Assets portfolios created by selected handler");
        var path = "/api/v1/io-t-devices/" + deviceId;
        await M38DeviceReadDemo.Check(h, "company-owner", path, deviceId, HttpStatusCode.OK, label: "M3.10");
        await M38DeviceReadDemo.Check(h, "unrelated-company", path, deviceId, HttpStatusCode.NotFound, other: true, label: "M3.10");
        Assert.Equal(0, h.ForbiddenCalls);
        Console.WriteLine("PASS M3.10 authorized Company individual GET 200 exact eight-field DTO; other Company 404; inventory/outbox unchanged; no Assets modification events or external calls");
    }
}
