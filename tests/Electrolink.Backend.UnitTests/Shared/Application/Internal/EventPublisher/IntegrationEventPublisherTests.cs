using System.Text.Json;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Shared.Application.Internal.EventPublisher;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;
using Microsoft.EntityFrameworkCore;

namespace Electrolink.Backend.UnitTests.Shared.Application.Internal.EventPublisher;

public sealed class IntegrationEventPublisherTests : IDisposable
{
    private readonly RecordingContext _context = new();

    public void Dispose() => _context.Dispose();

    private static TechnicianEvaluationSubmittedIntegrationEvent Event() =>
        new("tech-1", 5, "great", new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc));

    private OutboxMessage StagedMessage() => _context.ChangeTracker.Entries<OutboxMessage>().Single().Entity;

    [Fact]
    public async Task PublishAsync_WithEvent_StagesOutboxMessageWithEventIdentity()
    {
        // Arrange
        var evt = Event();
        var sut = new IntegrationEventPublisher(_context);

        // Act
        await sut.PublishAsync(evt);

        // Assert
        var message = StagedMessage();
        Assert.Equal((evt.EventId, evt.OccurredOn, (DateTime?)null), (message.Id, message.OccurredOnUtc, message.ProcessedOnUtc));
    }

    [Fact]
    public async Task PublishAsync_WithEvent_StoresAssemblyQualifiedTypeAndJsonContent()
    {
        // Arrange
        var evt = Event();
        var sut = new IntegrationEventPublisher(_context);

        // Act
        await sut.PublishAsync(evt);

        // Assert
        var message = StagedMessage();
        Assert.Equal(typeof(TechnicianEvaluationSubmittedIntegrationEvent).AssemblyQualifiedName, message.Type);
        Assert.Equal(5, JsonDocument.Parse(message.Content).RootElement.GetProperty("Score").GetInt32());
    }

    [Fact]
    public async Task PublishAsync_WithEvent_SavesChangesOnce()
    {
        // Arrange
        var sut = new IntegrationEventPublisher(_context);

        // Act
        await sut.PublishAsync(Event());

        // Assert
        Assert.Equal(1, _context.SaveCount);
    }

    /// <summary>Real model with an unreachable Npgsql connection; persistence is intercepted so no database is needed.</summary>
    private sealed class RecordingContext() : AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit;Username=unit;Password=unit", o => o.UseNetTopologySuite()).Options)
    {
        public int SaveCount { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
