using System.Data.Common;
using Hampcoders.Electrolink.API.AlertManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Hampcoders.Electrolink.API.StoreManagement.Domain.Model;
namespace Hampcoders.Electrolink.API.AlertManagement.Infrastructure;

/// <summary>Last change sequence handed out for a store (one row per store, created on its first alert change).</summary>
public sealed class StoreAlertSequence
{
    public string StoreId { get; private set; } = null!;
    public long LastSequence { get; private set; }
    private StoreAlertSequence() { }
}

/// <summary>
///     Allocates per-store alert change sequences inside the transaction that persists the change. The counter row is
///     incremented with <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>, which keeps the row locked until commit, so
///     two writers of the same store are serialized and sequences become visible in commit order. A reader that sees
///     <c>LastSequence = N</c> can therefore rely on every change with a sequence &lt;= N being committed (rolled-back
///     changes leave gaps, never reorderings).
/// </summary>
public static class AlertSequencer
{
    public const string Schema = "alert_management";
    public const string Table = "store_alert_sequences";

    /// <summary>Alerts of the change tracker that are new or changed (other than their sequence).</summary>
    public static List<Alert> PendingChanges(ChangeTracker tracker) =>
        tracker.Entries<Alert>()
            .Where(e => e.State == EntityState.Added
                        || (e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(Alert.Sequence))))
            .Select(e => e.Entity).ToList();

    /// <summary>Stamps the alerts. Requires an open transaction on the context.</summary>
    public static async Task StampAsync(DbContext context, IReadOnlyCollection<Alert> alerts, CancellationToken ct)
    {
        if (alerts.Count == 0) return;
        var equipmentIds = alerts.Select(a => a.EquipmentId).Distinct().ToList();
        var storeOfEquipment = await StoreIdsOfEquipment(context, equipmentIds, ct);
        // Fixed lock order across stores avoids deadlocks between transactions touching several stores.
        foreach (var group in alerts.Where(a => storeOfEquipment.ContainsKey(a.EquipmentId))
                     .GroupBy(a => storeOfEquipment[a.EquipmentId]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
            var last = await Allocate(context.Database, group.Key, ordered.Count, ct);
            var next = last - ordered.Count + 1;
            foreach (var alert in ordered) alert.AssignSequence(next++);
        }
    }

    /// <summary>Reserves <paramref name="count"/> sequences for the store and returns the last one.</summary>
    public static async Task<long> Allocate(DatabaseFacade database, string storeId, int count, CancellationToken ct)
    {
        var transaction = database.CurrentTransaction?.GetDbTransaction()
                          ?? throw new InvalidOperationException("Alert sequences must be allocated inside a transaction.");
        await using DbCommand command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {Schema}.{Table} (store_id, last_sequence) VALUES (@store, @count) " +
            $"ON CONFLICT (store_id) DO UPDATE SET last_sequence = {Schema}.{Table}.last_sequence + EXCLUDED.last_sequence " +
            "RETURNING last_sequence";
        AddParameter(command, "store", storeId);
        AddParameter(command, "count", (long)count);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async Task<Dictionary<string, string>> StoreIdsOfEquipment(DbContext context, List<string> equipmentIds, CancellationToken ct)
    {
        var rows = await (from e in context.Set<ElectricalEquipment>().AsNoTracking()
                          join a in context.Set<StoreArea>().AsNoTracking() on e.AreaId equals a.Id
                          where equipmentIds.Contains(e.Id)
                          select new { e.Id, a.StoreId }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.StoreId, StringComparer.Ordinal);
    }
}
