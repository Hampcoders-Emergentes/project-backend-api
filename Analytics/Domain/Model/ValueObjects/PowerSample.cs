namespace Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;

/// <summary>Instantaneous active power of one equipment at a capture time (UTC).</summary>
public sealed record PowerSample(string EquipmentId, DateTime CapturedAtUtc, double ActivePowerKw);
