using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;

/// <summary>Design-time only (dotnet ef); see <c>AppDbContextFactory</c>.</summary>
public sealed class StoreManagementDbContextFactory : IDesignTimeDbContextFactory<StoreManagementDbContext>
{
    public StoreManagementDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ELECTROLINK_DESIGN_CONNECTION")
                         ?? "Host=localhost;Database=electrolink_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<StoreManagementDbContext>().UseNpgsql(connection).Options;
        return new StoreManagementDbContext(options);
    }
}
