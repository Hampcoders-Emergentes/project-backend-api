using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Interfaces.REST.Transform;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.Analytics.Interfaces.REST.Transform;

public class TechnicianMetricsResourceFromEntityAssemblerTests
{
    [Fact]
    public void ToResourceFromEntity_WithInitializedMetrics_MapsIdentityAndPeriod()
    {
        // Arrange
        var period = DateRange.CurrentMonth();
        var metrics = TechnicianMetrics.InitializeForPeriod(TechnicianId.From(Ids.Technician), period);

        // Act
        var resource = TechnicianMetricsResourceFromEntityAssembler.ToResourceFromEntity(metrics);

        // Assert
        Assert.Equal((metrics.MetricsId.Value, Ids.Technician, period.Start, period.End),
            (resource.MetricsId, resource.TechnicianId, resource.PeriodStart, resource.PeriodEnd));
    }

    [Fact]
    public void ToResourceFromEntity_AfterCompletedService_MapsCountersAndRevenue()
    {
        // Arrange
        var metrics = TechnicianMetrics.InitializeForPeriod(TechnicianId.From(Ids.Technician), DateRange.CurrentMonth());
        metrics.ApplyServiceCompleted("client-1", Money.Of(80, "PEN"), TimeSpan.FromHours(1), true);

        // Act
        var resource = TechnicianMetricsResourceFromEntityAssembler.ToResourceFromEntity(metrics);

        // Assert
        Assert.Equal((1, 1, 80m, "PEN", TimeSpan.FromHours(1)),
            (resource.CompletedServicesCount, resource.IoTServicesCount, resource.TotalRevenueAmount, resource.TotalRevenueCurrency, resource.AverageResponseTime));
    }
}
