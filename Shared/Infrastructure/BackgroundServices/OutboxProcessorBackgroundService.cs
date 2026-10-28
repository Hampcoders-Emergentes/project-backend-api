using System.Text.Json;
using MediatR;
using Cortex.Mediator.Notifications;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.BackgroundServices;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider; 

    public OutboxProcessorBackgroundService(ILogger<OutboxProcessorBackgroundService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor Background Service running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOutboxMessagesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); 
        }

        _logger.LogInformation("Outbox Processor Background Service stopped.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var outboxMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(100)
            .ToListAsync(stoppingToken);

        var now = DateTime.UtcNow;

        foreach (var message in outboxMessages)
        {
            if (stoppingToken.IsCancellationRequested) return;

            try
            {
                var integrationEventType = Type.GetType(message.Type);
                if (integrationEventType is null)
                    throw new InvalidOperationException($"No se pudo resolver el tipo: {message.Type}");

                // ¡CAMBIO CLAVE AQUÍ! Cast a IIntegrationEvent
                var integrationEvent = JsonSerializer.Deserialize(message.Content, integrationEventType) as IIntegrationEvent;
                if (integrationEvent is null)
                    throw new InvalidOperationException($"No se pudo deserializar el contenido para el tipo: {message.Type}");

                // MediatR Publish espera INotification, y IIntegrationEvent hereda de IEvent, que hereda de INotification.
                // Así que el publish es correcto.
                await mediator.Publish(integrationEvent, stoppingToken);

                message.ProcessedOnUtc = now;
                message.Error = null;
                _logger.LogInformation($"Publicado mensaje outbox (MediatR): {integrationEvent.GetType().Name} - {integrationEvent.EventId}");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error procesando mensaje outbox ID {message.Id}.");
                message.Error = ex.Message;
                message.ProcessedOnUtc = now;
            }
        }

        if (outboxMessages.Count > 0)
            await dbContext.SaveChangesAsync(stoppingToken);
    }
}
