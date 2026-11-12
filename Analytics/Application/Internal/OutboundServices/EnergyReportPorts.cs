using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Analytics.Application.Internal.OutboundServices;

/// <summary>Outbound port: electricity tariff that applies to a store.</summary>
public interface IUtilityRateProvider
{
    Task<UtilityRateSchedule> ForStore(string storeId, CancellationToken ct);
}

/// <summary>Outbound port: durable, time-limited storage of generated report files.</summary>
public interface IEnergyReportStorage
{
    Task Store(EnergyReport report, CancellationToken ct);
    Task<EnergyReport?> Find(string storeId, string reportId, CancellationToken ct);
    Task<int> PurgeExpired(DateTime nowUtc, CancellationToken ct);
}

public sealed record EnergyReportDailyRow(DateOnly Day, string EquipmentId, string EquipmentName, double Kwh, double PeakKw, decimal Cost);
public sealed record EnergyReportEquipmentRow(string EquipmentId, string EquipmentName, double Kwh, double PeakKw, decimal Cost, double SharePercent);
public sealed record EnergyReportPeakHour(DateTime LocalHourStart, double Kwh, decimal Cost);
public sealed record EnergyReportHourProfile(int LocalHour, double AverageKwh, bool IsPeakTariff);

/// <summary>Content of an energy efficiency report, independent of the file format.</summary>
/// <param name="MaxDemandKw">Highest store-wide demand, as the average over a clock hour.</param>
public sealed record EnergyReportData(string StoreId, DateTime FromUtc, DateTime ToUtc, DateTime GeneratedAtUtc, string TimeZone,
    string Currency, decimal BaseRatePerKwh, double TotalKwh, double PeakKwh, double OffPeakKwh, decimal TotalCost, decimal PeakCost,
    decimal OffPeakCost, double MaxDemandKw, IReadOnlyList<EnergyReportEquipmentRow> Equipment, IReadOnlyList<EnergyReportDailyRow> Daily,
    IReadOnlyList<EnergyReportPeakHour> PeakDemandHours, IReadOnlyList<EnergyReportHourProfile> HourOfDayProfile);

/// <summary>Outbound port: renders report content into a file (CSV, PDF).</summary>
public interface IEnergyReportRenderer
{
    ReportFile Render(EnergyReportData data, string format);
}
