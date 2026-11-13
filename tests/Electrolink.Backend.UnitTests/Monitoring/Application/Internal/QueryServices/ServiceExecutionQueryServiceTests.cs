using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Assets.Interfaces.ACL;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.Monitoring.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Events;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.Queries;
using Hampcoders.Electrolink.API.Monitoring.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Monitoring.Domain.Repositories;
using Hampcoders.Electrolink.API.Processing.Domain.Model.Events;
using Hampcoders.Electrolink.API.Processing.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.Monitoring.MonitoringTestData;
using ServiceCompletedEventHandler = Hampcoders.Electrolink.API.Monitoring.Application.Internal.EventHandlers.ServiceCompletedEventHandler;

namespace Electrolink.Backend.UnitTests.Monitoring.Application.Internal.QueryServices;

public class ServiceExecutionQueryServiceTests
{
    private readonly Fake<IServiceExecutionRepository> _repository = new();

    private ServiceExecutionQueryService CreateSut() => new(_repository.Object);

    [Fact]
    public async Task Handle_GetById_ReturnsRepositoryExecution()
    {
        // Arrange
        var execution = Notified();
        _repository.Returns(nameof(IServiceExecutionRepository.FindByIdAsync), execution);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceExecutionByIdQuery(execution.Id));

        // Assert
        Assert.Same(execution, result);
    }

    [Fact]
    public async Task Handle_GetWorkLog_ReturnsRepositoryExecution()
    {
        // Arrange
        var execution = Notified();
        _repository.Returns(nameof(IServiceExecutionRepository.FindByIdAsync), execution);
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetWorkLogQuery(execution.Id));

        // Assert
        Assert.Same(execution, result);
    }

    [Fact]
    public async Task Handle_GetAssignedByTechnician_QueriesByTechnician()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new GetAssignedServicesByTechnicianQuery(Technician));

        // Assert
        Assert.Equal(Technician, _repository.CallsTo(nameof(IServiceExecutionRepository.FindByTechnicianIdAsync)).Single().Arg<TechnicianId>());
    }

    [Fact]
    public async Task Handle_GetHistoryByTechnician_ReturnsTechnicianExecutions()
    {
        // Arrange
        var execution = Notified();
        _repository.Returns(nameof(IServiceExecutionRepository.FindByTechnicianIdAsync), new[] { execution });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceHistoryByTechnicianQuery(Technician));

        // Assert
        Assert.Same(execution, Assert.Single(result));
    }

    [Fact]
    public async Task Handle_GetActiveByClient_QueriesActiveByOwner()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(new GetActiveServiceByClientQuery(Owner));

        // Assert
        Assert.Equal(Owner, _repository.CallsTo(nameof(IServiceExecutionRepository.FindActiveByOwnerAsync)).Single().Arg<ClientIdentity>());
    }

    [Fact]
    public async Task Handle_GetHistoryByClient_ReturnsOwnerExecutions()
    {
        // Arrange
        _repository.Returns(nameof(IServiceExecutionRepository.FindByOwnerAsync), new[] { Notified(), Notified() });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetServiceHistoryByClientQuery(Owner));

        // Assert
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task Handle_GetNoShowAlert_ReturnsOnlyOverdueUnstartedExecutions()
    {
        // Arrange
        var overdue = Notified(scheduledAt: DateTime.UtcNow.AddMinutes(-45));
        var recent = Notified(scheduledAt: DateTime.UtcNow.AddMinutes(-10));
        var started = InProgress();
        _repository.Returns(nameof(IServiceExecutionRepository.FindActiveServiceExecutionAsync), new[] { overdue, recent, started });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetNoShowAlertQuery());

        // Assert
        Assert.Same(overdue, Assert.Single(result));
    }
}
