using Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;
using Hampcoders.Electrolink.API.Analytics.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;

/// <param name="Format">csv or pdf.</param>
public sealed record GenerateEnergyReportCommand(UserId Actor, string StoreId, DateTime FromUtc, DateTime ToUtc, string Format);
public sealed record GetEnergyReportFileQuery(UserId Actor, string StoreId, string ReportId);

public sealed record EnergyReportMetadata(string ReportId, string StoreId, string Format, string FileName, string ContentType,
    long SizeBytes, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc, DateTime ExpiresAtUtc);

/// <summary>The report does not exist, belongs to another store, or has expired.</summary>
public sealed class EnergyReportNotFoundException() : Exception("Report not found.");

/// <summary>
///     Builds an energy efficiency report (daily equipment breakdown, peak demand hours, costs), renders it and stores the
///     file for <see cref="EnergyReport.Lifetime"/> so it can be downloaded separately.
/// </summary>
public sealed class EnergyReportCommandService(IStoreMeasurementPort measurements, IUtilityRateProvider rates,
    IEnergyReportRenderer renderer, IEnergyReportStorage storage)
{
    public static readonly IReadOnlyList<string> Formats = ["csv", "pdf"];
    private const int PeakHoursListed = 5;

    public async Task<EnergyReportMetadata> Handle(GenerateEnergyReportCommand command, CancellationToken ct)
    {
        var format = (command.Format ?? "").Trim().ToLowerInvariant();
        if (!Formats.Contains(format)) throw new ArgumentException("format must be csv or pdf.");
        var now = DateTime.UtcNow;
        if (command.FromUtc >= now) throw new ArgumentException("from must be in the past.");
        var toUtc = command.ToUtc > now ? now : command.ToUtc;

        var data = await Build(command.Actor, command.StoreId, command.FromUtc, toUtc, now, ct);
        var file = renderer.Render(data, format);
        await storage.PurgeExpired(now, ct);
        var report = EnergyReport.Create(command.StoreId, command.Actor.Value, format, file.FileName, file.ContentType, file.Content,
            command.FromUtc, toUtc, now);
        await storage.Store(report, ct);
        return Metadata(report);
    }

    public async Task<EnergyReport> Handle(GetEnergyReportFileQuery query, CancellationToken ct)
    {
        if (!await measurements.CanAccess(query.Actor, query.StoreId, ct)) throw new StoreNotAccessibleException();
        var report = await storage.Find(query.StoreId, query.ReportId, ct);
        if (report == null || report.IsExpired(DateTime.UtcNow)) throw new EnergyReportNotFoundException();
        return report;
    }

    public static EnergyReportMetadata Metadata(EnergyReport r) => new(r.Id, r.StoreId, r.Format, r.FileName, r.ContentType,
        r.SizeBytes, r.FromUtc, r.ToUtc, r.GeneratedAtUtc, r.ExpiresAtUtc);

    private async Task<EnergyReportData> Build(UserId actor, string storeId, DateTime fromUtc, DateTime toUtc, DateTime now, CancellationToken ct)
    {
        var data = await measurements.Load(actor, storeId, fromUtc - EnergyIntegrator.DefaultMaxGap, toUtc, ct);
        var tariff = await rates.ForStore(storeId, ct);
        var equipment = data.EquipmentNames.Select(kv =>
        {
            var samples = data.Samples.Where(s => s.EquipmentId == kv.Key).OrderBy(s => s.CapturedAtUtc).ToList();
            var priced = EnergyCostCalculator.Price(EnergyIntegrator.Integrate(samples, fromUtc, toUtc, Granularity.Hourly), tariff);
            var inWindow = samples.Where(s => s.CapturedAtUtc >= fromUtc && s.CapturedAtUtc < toUtc).ToList();
            return (Id: kv.Key, Name: kv.Value, Priced: priced, Samples: inWindow);
        }).ToList();

        var storeHourly = equipment.SelectMany(e => e.Priced).GroupBy(b => b.StartUtc).OrderBy(g => g.Key)
            .Select(g => new PricedBucket(g.Key, g.Sum(b => b.Kwh), g.Sum(b => b.Cost), g.First().IsPeak)).ToList();
        var total = EnergyCostCalculator.Summarize(storeHourly, tariff);

        var daily = equipment.SelectMany(e => EnergyCostCalculator.Daily(e.Priced, tariff).Select(d => new EnergyReportDailyRow(d.Day, e.Id,
                e.Name, Math.Round(d.Kwh, 4), Math.Round(e.Samples.Where(s => DateOnly.FromDateTime(tariff.ToLocal(s.CapturedAtUtc)) == d.Day)
                    .Select(s => s.ActivePowerKw).DefaultIfEmpty(0).Max(), 3), EnergyCostCalculator.Round(d.Cost))))
            .OrderBy(r => r.Day).ThenBy(r => r.EquipmentName, StringComparer.Ordinal).ToList();

        var perEquipment = equipment.Select(e =>
        {
            var kwh = e.Priced.Sum(b => b.Kwh);
            return new EnergyReportEquipmentRow(e.Id, e.Name, Math.Round(kwh, 4),
                Math.Round(e.Samples.Select(s => s.ActivePowerKw).DefaultIfEmpty(0).Max(), 3), EnergyCostCalculator.Round(e.Priced.Sum(b => b.Cost)),
                total.TotalKwh == 0 ? 0 : Math.Round(kwh / total.TotalKwh * 100, 2));
        }).OrderByDescending(e => e.Kwh).ThenBy(e => e.EquipmentName, StringComparer.Ordinal).ToList();

        // An hourly bucket's kWh equals the average demand in kW over that hour.
        var peakHours = storeHourly.OrderByDescending(b => b.Kwh).ThenBy(b => b.StartUtc).Take(PeakHoursListed)
            .Select(b => new EnergyReportPeakHour(tariff.ToLocal(b.StartUtc), Math.Round(b.Kwh, 4), EnergyCostCalculator.Round(b.Cost))).ToList();
        var days = Math.Max(1, (toUtc - fromUtc).TotalDays);
        var profile = Enumerable.Range(0, 24).Select(hour => new EnergyReportHourProfile(hour,
            Math.Round(storeHourly.Where(b => tariff.ToLocal(b.StartUtc).Hour == hour).Sum(b => b.Kwh) / days, 4),
            tariff.IsPeakLocalHour(hour))).ToList();

        return new(storeId, fromUtc, toUtc, now, tariff.TimeZone.Id, tariff.Currency, tariff.BaseRatePerKwh, Math.Round(total.TotalKwh, 4),
            Math.Round(total.PeakKwh, 4), Math.Round(total.OffPeakKwh, 4), total.TotalCost, total.PeakCost, total.OffPeakCost,
            Math.Round(storeHourly.Select(b => b.Kwh).DefaultIfEmpty(0).Max(), 3),
            perEquipment, daily, peakHours, profile);
    }
}
