using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.QueryServices;

public sealed record GetStoreConsumptionQuery(UserId Actor, string StoreId, DateTime FromUtc, DateTime ToUtc, Granularity Granularity);
public sealed record ExportStoreConsumptionReportQuery(GetStoreConsumptionQuery Consumption, ExportFormat Format);

public sealed class StoreConsumptionQueryService(IStoreMeasurementPort measurements, IConsumptionReportExporter exporter)
{
    public async Task<StoreEnergyMetric> Handle(GetStoreConsumptionQuery query, CancellationToken ct)
    {
        // Samples just before the window start are needed so the first interval is integrated correctly.
        var data = await measurements.Load(query.Actor, query.StoreId, query.FromUtc - EnergyIntegrator.DefaultMaxGap, query.ToUtc, ct);

        var perEquipment = data.Samples.GroupBy(s => s.EquipmentId).Select(g =>
        {
            var ordered = g.OrderBy(s => s.CapturedAtUtc).ToList();
            var series = EnergyIntegrator.Integrate(ordered, query.FromUtc, query.ToUtc, query.Granularity);
            var inWindow = ordered.Where(s => s.CapturedAtUtc >= query.FromUtc && s.CapturedAtUtc < query.ToUtc).ToList();
            return new EquipmentConsumption(g.Key, data.EquipmentNames.GetValueOrDefault(g.Key, g.Key), EnergyIntegrator.Total(series),
                inWindow.Count == 0 ? 0 : inWindow.Max(s => s.ActivePowerKw), inWindow.Count, series);
        }).OrderBy(e => e.EquipmentName, StringComparer.Ordinal).ThenBy(e => e.EquipmentId, StringComparer.Ordinal).ToList();

        // Equipment without samples still appears, with zero consumption.
        foreach (var (id, name) in data.EquipmentNames.Where(kv => perEquipment.All(e => e.EquipmentId != kv.Key)).OrderBy(kv => kv.Value, StringComparer.Ordinal))
            perEquipment.Add(new EquipmentConsumption(id, name, 0, 0, 0, []));

        var storeSeries = perEquipment.SelectMany(e => e.Series).GroupBy(b => b.StartUtc).OrderBy(g => g.Key)
            .Select(g => new ConsumptionBucket(g.Key, g.Sum(b => b.Kwh))).ToList();
        return new(query.StoreId, query.FromUtc, query.ToUtc, query.Granularity, perEquipment.Sum(e => e.Kwh), perEquipment, storeSeries);
    }

    public async Task<ReportFile> Handle(ExportStoreConsumptionReportQuery query, CancellationToken ct) =>
        exporter.Export(await Handle(query.Consumption, ct), query.Format);
}
