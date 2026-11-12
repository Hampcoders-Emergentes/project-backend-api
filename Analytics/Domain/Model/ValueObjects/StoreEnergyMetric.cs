using Hampcoders.Electrolink.API.Analytics.Domain.Model.Enums;

namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

public sealed record ConsumptionBucket(DateTime StartUtc, double Kwh);

public sealed record EquipmentConsumption(string EquipmentId, string EquipmentName, double Kwh, double PeakKw, int SampleCount,
    IReadOnlyList<ConsumptionBucket> Series);

/// <summary>Energy consumed by a store over [FromUtc, ToUtc), integrated from measured power.</summary>
public sealed record StoreEnergyMetric(string StoreId, DateTime FromUtc, DateTime ToUtc, Granularity Granularity,
    double TotalKwh, IReadOnlyList<EquipmentConsumption> Equipment, IReadOnlyList<ConsumptionBucket> Series);
