using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hampcoders.Electrolink.API.Processing.Infrastructure.Pipeline.Middleware.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireEdgeApiKeyAttribute : AllowEdgeApiKeyAttribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        var expectedApiKey = config?.GetValue<string>("EdgeApi:ApiKey");

        if (string.IsNullOrEmpty(expectedApiKey))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            return;
        }

        var providedKey = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey) || providedKey != expectedApiKey)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Unauthorized: invalid or missing API key." });
        }
    }
}
