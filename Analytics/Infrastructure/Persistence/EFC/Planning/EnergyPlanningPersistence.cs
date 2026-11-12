using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Analytics.Infrastructure.Persistence.EFC.Planning;

public static class EnergyPlanningMapping
{
    public static void MapEnergyPlanning(this ModelBuilder model)
    {
        var h = model.Entity<StoreOpeningSchedule>();
        h.ToTable("store_opening_hours", "analytics"); h.HasKey(x => x.Id);
        h.Property(x => x.Id).HasMaxLength(38);
        h.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        h.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(10);
        h.Ignore(x => x.IsAroundTheClock); h.Ignore(x => x.EndsNextDay);
        h.HasIndex(x => new { x.StoreId, x.DayOfWeek }).IsUnique();
        h.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);

        var b = model.Entity<StoreEnergyBudget>();
        b.ToTable("store_energy_budgets", "analytics"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasMaxLength(39);
        b.Property(x => x.StoreId).HasMaxLength(38).IsRequired();
        b.Property(x => x.UpdatedByUserId).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.Year, x.Month }).IsUnique();
        b.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EnergyPlanningRepository(StoreManagementDbContext context) : IEnergyPlanningRepository
{
    public Task<List<StoreOpeningSchedule>> OpeningHours(string storeId, CancellationToken ct) =>
        context.Set<StoreOpeningSchedule>().AsNoTracking().Where(h => h.StoreId == storeId).ToListAsync(ct);

    public async Task ReplaceOpeningHours(string storeId, IReadOnlyList<StoreOpeningSchedule> days, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.Set<StoreOpeningSchedule>().Where(h => h.StoreId == storeId).ExecuteDeleteAsync(ct);
        context.Set<StoreOpeningSchedule>().AddRange(days);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<StoreEnergyBudget?> Budget(string storeId, int year, int month, CancellationToken ct) =>
        context.Set<StoreEnergyBudget>().SingleOrDefaultAsync(b => b.StoreId == storeId && b.Year == year && b.Month == month, ct);

    public void Add(StoreEnergyBudget budget) => context.Set<StoreEnergyBudget>().Add(budget);

    public Task Save(CancellationToken ct) => context.SaveChangesAsync(ct);
}
