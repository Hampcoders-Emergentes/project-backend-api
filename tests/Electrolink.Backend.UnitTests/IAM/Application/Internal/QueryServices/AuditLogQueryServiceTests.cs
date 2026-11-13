using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Events;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Exceptions;
using Hampcoders.Electrolink.API.StoreManagement.Interfaces.ACL;
using Microsoft.Extensions.Logging.Abstractions;
using static Electrolink.Backend.UnitTests.IAM.IamTestData;

namespace Electrolink.Backend.UnitTests.IAM.Application.Internal.QueryServices;

public class AuditLogQueryServiceTests
{
    private readonly Fake<IAuditLogRepository> _repository = new();
    private readonly Fake<IStoreAccessFacade> _stores = new Fake<IStoreAccessFacade>().Returns(nameof(IStoreAccessFacade.CanManage), true);

    private AuditLogQueryService CreateSut() => new(_repository.Object, new ExternalStoreAccessService(_stores.Object));

    private static GetStoreAuditLogsQuery Query(int page = 1, int pageSize = 2, string? action = null, string? status = null, string? from = null,
        string? to = null, string? operatorId = null, string? resource = null) =>
        new(Ids.Actor, Ids.Store, from, to, action, status, page, pageSize, operatorId, resource);

    private static AuditLogEntry Entry(int n) => AuditLogEntry.Record(Ids.User, Ids.Store, AuditAction.Read, "api/v1/stores", "GET",
        "/api/v1/stores/" + n, 200, AuditStatus.Success, "127.0.0.1", DateTime.UtcNow);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Handle_WithInvalidPaging_ThrowsArgumentException(int page, int pageSize)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(page, pageSize), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithUnknownAction_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(action: "PURGE"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithUnknownStatus_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(status: "MAYBE"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithResourceContainingInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(resource: "api/v1;drop"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithOverlongOperatorId_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(operatorId: new string('x', 101)), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WithOnlyFromTimestamp_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(from: "2025-01-01T00:00:00Z"), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task Handle_WhenStoreNotManageable_ThrowsAuditStoreNotFoundException()
    {
        // Arrange
        _stores.Returns(nameof(IStoreAccessFacade.CanManage), false);
        var sut = CreateSut();

        // Act
        var act = () => sut.Handle(Query(), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<AuditStoreNotFoundException>(act);
    }

    [Fact]
    public async Task Handle_WithLowercaseFilters_NormalizesFiltersToUppercase()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Query(action: " read ", status: "success", from: "2025-01-01T00:00:00Z", to: "2025-01-02T00:00:00Z"),
            CancellationToken.None);

        // Assert
        var filter = _repository.Calls.Single().Arg<AuditLogFilter>();
        Assert.Equal(("READ", "SUCCESS", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)), (filter.Action, filter.Status, filter.FromUtc));
    }

    [Fact]
    public async Task Handle_OnSecondPage_RequestsOneExtraRowAfterOffset()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        await sut.Handle(Query(page: 2, pageSize: 2), CancellationToken.None);

        // Assert
        var call = _repository.Calls.Single();
        Assert.Equal((2, 3), (call.Arg<int>(1), call.Arg<int>(2)));
    }

    [Fact]
    public async Task Handle_WhenMoreRowsThanPageSize_ReportsHasMore()
    {
        // Arrange
        _repository.Returns(nameof(IAuditLogRepository.ListAsync), new List<AuditLogEntry> { Entry(1), Entry(2), Entry(3) });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(pageSize: 2), CancellationToken.None);

        // Assert
        Assert.Equal((2, true), (result.Items.Count, result.HasMore));
    }

    [Fact]
    public async Task Handle_WhenRowsFitPage_ReportsNoMore()
    {
        // Arrange
        _repository.Returns(nameof(IAuditLogRepository.ListAsync), new List<AuditLogEntry> { Entry(1) });
        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Query(pageSize: 2), CancellationToken.None);

        // Assert
        Assert.Equal(("/api/v1/stores/1", false), (Assert.Single(result.Items).Path, result.HasMore));
    }
}
