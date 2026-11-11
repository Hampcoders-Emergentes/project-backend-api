using Hampcoders.Electrolink.API.AlertManagement.Interfaces.ACL;
using Hampcoders.Electrolink.API.StoreManagement.Application;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Reporting;

namespace Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Services;

/// <summary>Anti-corruption adapter over the AlertManagement history ACL.</summary>
public sealed class AlertManagementComplianceAdapter(IStoreAlertHistoryFacade alerts) : IComplianceAlertPort
{
    public async Task<IReadOnlyList<SafetyAlertRecord>> History(string storeId, DateTime fromUtc, DateTime toUtc, bool criticalOnly, CancellationToken ct) =>
        (await alerts.History(storeId, fromUtc, toUtc, criticalOnly, ct)).Select(a => new SafetyAlertRecord(a.AlertId, a.EquipmentId, a.EquipmentName,
            a.AreaName, a.AnomalyType, a.Severity, a.Status, a.CreatedAtUtc, a.AcknowledgedAtUtc, a.AssignedAtUtc, a.AssignedTechnicianName,
            a.ResolvedAtUtc, a.ResolvedByUserId, a.ResolutionNotes, a.OnDutyWorkerName)).ToList();
}

public sealed class PngSignatureImageReader : ISignatureImageReader
{
    public (int Width, int Height) Inspect(byte[] png)
    {
        var image = RasterImage.FromPng(png, maxPixels: 2_000_000);
        return (image.Width, image.Height);
    }
}
