using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAccessRoleAttribute(params AccessRole[] roles) : Attribute, IAuthorizationFilter
{
    private readonly AccessRole[] _roles = roles;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var allowAnonymous = context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any();
        if (allowAnonymous) return;

        var user = (User?)context.HttpContext.Items["User"];
        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (_roles.Length > 0 && !_roles.Contains(user.Role))
            context.Result = new ObjectResult(new { message = "Your access role does not allow this action." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
    }
}
