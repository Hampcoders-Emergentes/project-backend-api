using Hampcoders.Electrolink.API.Profiles.Domain.Model.Events;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST;
using Hampcoders.Electrolink.API.Profiles.Interfaces.REST.Resources;
using MediatR;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Electrolink.Backend.Characterization.Tests.StoreManagement;

// Opt-in observer only. It neither replaces services/handlers nor logs requests or credentials.
internal sealed class CompanyCompletionTrace : IAsyncActionFilter, IOrderedFilter, INotificationHandler<ProfileCompletedEvent>
{
    public int Order => int.MinValue; // Observe ApiController's model-state short circuit too.
    internal List<CompletionStep> Steps { get; } = new();
    internal List<ProfileCompletedEvent> Events { get; } = new();
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor { MethodInfo.Name: nameof(ProfilesController.CompleteAsCompany) })
        { await next(); return; }
        var before = Events.Count; var valid = context.ModelState.IsValid;
        var resource = (context.ActionArguments.TryGetValue("resource", out var body) ? body : null) as CompleteProfileAsCompanyResource;
        var executed = await next();
        var application = Events.Count > before || executed.Exception?.StackTrace?.Contains("ProfileCommandService.Handle", StringComparison.Ordinal) == true;
        Steps.Add(new(valid, !executed.Canceled, application, executed.Exception?.GetType().Name, resource));
    }
    public Task Handle(ProfileCompletedEvent notification, CancellationToken cancellationToken)
    { Events.Add(notification); return Task.CompletedTask; }
}
internal sealed record CompletionStep(bool ModelValid, bool ControllerReached, bool ApplicationReached, string? ExceptionType,
    CompleteProfileAsCompanyResource? Resource);
