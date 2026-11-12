using Hampcoders.Electrolink.API.Shared.Application.Notifications;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Microsoft.Extensions.Options;

namespace Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;

/// <summary>Bound from "Subscriptions:DemoRequests": where demo requests are sent.</summary>
public sealed class DemoRequestSettings
{
    /// <summary>Sales inbox notified of each demo request. Without it requests are stored and logged only.</summary>
    public string? SalesInbox { get; set; }
    /// <summary>Requests accepted per email address and day.</summary>
    public int MaxRequestsPerEmailPerDay { get; set; } = 3;
}

public sealed record SubmitContactRequestCommand(string Name, string Company, string Email, string? Phone, string Message, string? SourceIp);

public sealed record ContactRequestReceiptResource(string RequestId, string Status, DateTime ReceivedAtUtc);

public sealed class ContactRequestLimitException() : Exception("Too many requests from this email address today. Please try again tomorrow.");

/// <summary>Stores a demo request and notifies the sales inbox; the prospect gets a confirmation email.</summary>
public sealed class ContactRequestCommandService(IContactRequestRepository repository, IEmailSender email, IOptions<DemoRequestSettings> settings,
    ILogger<ContactRequestCommandService> logger)
{
    public async Task<ContactRequestReceiptResource> Handle(SubmitContactRequestCommand command, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var request = ContactRequest.Submit(command.Name, command.Company, command.Email, command.Phone, command.Message, command.SourceIp, now);
        if (await repository.CountFromEmailSince(request.Email, now.AddDays(-1), ct) >= Math.Max(1, settings.Value.MaxRequestsPerEmailPerDay))
            throw new ContactRequestLimitException();
        repository.Add(request);
        await repository.Save(ct);

        var inbox = settings.Value.SalesInbox;
        if (string.IsNullOrWhiteSpace(inbox))
        {
            logger.LogInformation("[Subscriptions] Demo request {RequestId} stored; no sales inbox configured", request.Id);
        }
        else
        {
            try
            {
                await email.SendAsync(new EmailMessage(inbox, $"[Electrolink] Demo request - {request.Company}", string.Join('\n',
                    $"Name: {request.Name}", $"Company: {request.Company}", $"Email: {request.Email}", $"Phone: {request.Phone ?? "-"}",
                    $"Received: {request.ReceivedAtUtc:yyyy-MM-dd HH:mm} UTC", $"Request: {request.Id}", "", request.Message)), ct);
                request.MarkNotified(DateTime.UtcNow);
                await repository.Save(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The request is stored; sales can still find it.
                logger.LogError(ex, "[Subscriptions] Sales notification failed for demo request {RequestId}", request.Id);
            }
        }

        try
        {
            await email.SendAsync(new EmailMessage(request.Email, "We received your Electrolink demo request",
                $"Hello {request.Name},\n\nThank you for your interest in Electrolink. Our team will contact you shortly to schedule a demo for {request.Company}.\n\nElectrolink"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[Subscriptions] Confirmation email failed for demo request {RequestId}", request.Id);
        }
        return new(request.Id, request.Status, request.ReceivedAtUtc);
    }
}
