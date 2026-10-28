using Microsoft.Extensions.DependencyInjection;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

public sealed class RealBackendModelTests
{
    [Fact]
    public void Legacy_and_store_contexts_resolve_their_own_typed_options_without_connecting()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=offline_m35;Username=synthetic", pg => pg.UseNetTopologySuite()));
        services.AddStoreManagement("Host=127.0.0.1;Port=1;Database=offline_m35;Username=synthetic");
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.StoreManagementDbContext>());
    }
    [Fact]
    public void Legacy_model_generates_ddl_offline_for_review_without_running_initialization()
    {
        var sql = M35PostgresDemo.LegacySchema();
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS postgis", sql);
        Assert.Contains("CREATE TABLE users", sql);
        Assert.Contains("CREATE TABLE profiles", sql);
        Assert.Contains("serial_number", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }
}
