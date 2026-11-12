using EntityFrameworkCore.CreatedUpdatedDate.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     Design-time only (dotnet ef). Generating migrations never opens a connection, so the default string is a placeholder;
///     override with ELECTROLINK_DESIGN_CONNECTION for `database update`.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ELECTROLINK_DESIGN_CONNECTION")
                         ?? "Host=localhost;Database=electrolink_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, o => o.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}
