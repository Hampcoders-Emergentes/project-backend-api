using Hampcoders.Electrolink.API.Profiles.Domain.Model.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions.Filters;

public class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        (int? statusCode, string? message) = context.Exception switch
        {
            UnauthorizedProfileAccessException => ((int?)StatusCodes.Status403Forbidden, context.Exception.Message),
            InvalidProfileStatusException => ((int?)StatusCodes.Status409Conflict, context.Exception.Message),
            DniAlreadyInUseException => ((int?)StatusCodes.Status409Conflict, context.Exception.Message),
            TaxIdAlreadyInUseException => ((int?)StatusCodes.Status409Conflict, context.Exception.Message),
            InvalidPhoneNumberException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            InvalidDniException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            InvalidTaxIdException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            InvalidDateOfBirthException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            UnderageUserException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            InvalidServiceAreaException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            InvalidBusinessRoleException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            AtLeastOneSpecialtyRequiredException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            AtLeastOneNotificationChannelRequiredException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            KeyNotFoundException => ((int?)StatusCodes.Status404NotFound, context.Exception.Message),
            ArgumentException => ((int?)StatusCodes.Status400BadRequest, context.Exception.Message),
            _ => (null, null)
        };

        if (statusCode.HasValue)
        {
            context.Result = new ObjectResult(new { message })
            {
                StatusCode = statusCode.Value
            };
            context.ExceptionHandled = true;
        }
    }
}