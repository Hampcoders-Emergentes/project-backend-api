using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Analytics.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Analytics.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Electrolink.Backend.UnitTests.Analytics.Application.Internal.CommandServices;

public class TechnicianMetricsCommandServiceTests
{
    private readonly Fake<ITechnicianMetricsRepository> _repository = new();
    private readonly Fake<IUnitOfWork> _unitOfWork = new();
    private readonly Fake<IMediator> _mediator = new();

    private TechnicianMetricsCommandService CreateSut() =>
        new(_repository.Object, _unitOfWork.Object, _mediator.Object, NullLogger<TechnicianMetricsCommandService>.Instance);

    private static TechnicianMetrics ExistingMetrics() =>
        TechnicianMetrics.InitializeForPeriod(TechnicianId.From(Ids.Technician), DateRange.CurrentMonth());

    [Fact]
    public async Task UpdateTechnicianMetricsAsync_WhenNoCurrentPeriod_AddsNewMetrics()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.UpdateTechnicianMetricsAsync(Ids.Technician, "client-1", 150, "PEN", TimeSpan.FromHours(2), false);

        // Assert
        var added = Assert.Single(_repository.CallsTo(nameof(ITechnicianMetricsRepository.AddAsync))).Arg<TechnicianMetrics>();
        Assert.Equal(1, added.CompletedServicesCount);
    }

    [Fact]
    public async Task UpdateTechnicianMetricsAsync_WhenCurrentPeriodExists_AppliesServiceToExistingMetrics()
    {
        // Arrange
        var existing = ExistingMetrics();
        _repository.Returns(nameof(ITechnicianMetricsRepository.FindCurrentPeriodByTechnicianAsync), existing);
        var sut = CreateSut();

        // Act
        await sut.UpdateTechnicianMetricsAsync(Ids.Technician, "client-1", 150, "PEN", TimeSpan.FromHours(2), true);

        // Assert
        Assert.Equal((1, 1, 150m), (existing.CompletedServicesCount, existing.IoTServicesCount, existing.TotalRevenue.Amount));
    }

    [Fact]
    public async Task UpdateTechnicianMetricsAsync_AfterCommit_PublishesAndClearsDomainEvents()
    {
        // Arrange
        var existing = ExistingMetrics();
        _repository.Returns(nameof(ITechnicianMetricsRepository.FindCurrentPeriodByTechnicianAsync), existing);
        var sut = CreateSut();

        // Act
        await sut.UpdateTechnicianMetricsAsync(Ids.Technician, "client-1", 150, "PEN", TimeSpan.FromHours(2), false);

        // Assert
        Assert.Equal(1, _mediator.CallCount(nameof(IMediator.Publish)));
        Assert.Empty(existing.DomainEvents);
    }

    [Fact]
    public async Task UpdateTechnicianMetricsAsync_WithMalformedTechnicianId_ThrowsInvalidIdException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.UpdateTechnicianMetricsAsync("bad-id", "client-1", 150, "PEN", TimeSpan.Zero, false);

        // Assert
        await Assert.ThrowsAsync<InvalidIdException>(act);
    }

    [Fact]
    public async Task UpdateTechnicianRatingMetricAsync_WhenNoCurrentPeriod_ThrowsInvalidOperationException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.UpdateTechnicianRatingMetricAsync(Ids.Technician, 4);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task UpdateTechnicianRatingMetricAsync_WithValidRating_UpdatesAverageRating()
    {
        // Arrange
        var existing = ExistingMetrics();
        _repository.Returns(nameof(ITechnicianMetricsRepository.FindCurrentPeriodByTechnicianAsync), existing);
        var sut = CreateSut();

        // Act
        await sut.UpdateTechnicianRatingMetricAsync(Ids.Technician, 4);

        // Assert
        Assert.Equal(4m, existing.AverageRating);
    }

    [Fact]
    public async Task UpdateTechnicianRatingMetricAsync_WithRatingAboveFive_ThrowsArgumentException()
    {
        // Arrange
        _repository.Returns(nameof(ITechnicianMetricsRepository.FindCurrentPeriodByTechnicianAsync), ExistingMetrics());
        var sut = CreateSut();

        // Act
        var act = () => sut.UpdateTechnicianRatingMetricAsync(Ids.Technician, 6);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task InitializeTechnicianMetricsPeriodAsync_WhenPeriodExists_DoesNotAddMetrics()
    {
        // Arrange
        _repository.Returns(nameof(ITechnicianMetricsRepository.FindByTechnicianAndPeriodAsync), ExistingMetrics());
        var sut = CreateSut();

        // Act
        await sut.InitializeTechnicianMetricsPeriodAsync(Ids.Technician);

        // Assert
        Assert.Equal(0, _repository.CallCount(nameof(ITechnicianMetricsRepository.AddAsync)));
    }

    [Fact]
    public async Task InitializeTechnicianMetricsPeriodAsync_WhenPeriodMissing_AddsAndCommitsMetrics()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.InitializeTechnicianMetricsPeriodAsync(Ids.Technician);

        // Assert
        Assert.Equal((1, 1), (_repository.CallCount(nameof(ITechnicianMetricsRepository.AddAsync)),
            _unitOfWork.CallCount(nameof(IUnitOfWork.CompleteAsync))));
    }
}
