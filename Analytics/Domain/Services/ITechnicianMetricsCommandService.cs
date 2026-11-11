namespace Hampcoders.Electrolink.API.Analytics.Domain.Services;

public interface ITechnicianMetricsCommandService
{
    Task UpdateTechnicianMetricsAsync(string technicianId, string clientId, decimal serviceRevenueAmount, string currency, TimeSpan responseTime, bool requiresIoTCertification);
    Task UpdateTechnicianRatingMetricAsync(string technicianId, decimal rating);
    Task InitializeTechnicianMetricsPeriodAsync(string technicianId);
}
