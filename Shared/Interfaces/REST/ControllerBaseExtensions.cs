using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Hampcoders.Electrolink.API.Shared.Interfaces.REST;

public static class ControllerBaseExtensions
{
    /// <summary>
    /// Extracts the authenticated user ID from JWT claims.
    /// </summary>
    public static int? GetAuthenticatedUserId(this ControllerBase controller)
    {
        var userIdClaim = 
            // Standard claims
            controller.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? controller.User.FindFirst("sub")?.Value
            
            // ASP.NET Identity claims (tu caso)
            ?? controller.User.FindFirst(ClaimTypes.Sid)?.Value
            ?? controller.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid")?.Value
            
            // Custom claims
            ?? controller.User.FindFirst("userId")?.Value
            ?? controller.User.FindFirst("id")?.Value;

        if (string.IsNullOrEmpty(userIdClaim))
            return null;

        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    /// <summary>
    /// Gets the authenticated user ID or throws UnauthorizedAccessException.
    /// </summary>
    public static int GetAuthenticatedUserIdOrThrow(this ControllerBase controller)
    {
        var userId = controller.GetAuthenticatedUserId();
        
        // The message becomes the "detail" of the 403 error envelope, so it must not echo the token's claims.
        if (!userId.HasValue)
            throw new UnauthorizedAccessException("User ID not found in authentication token.");

        return userId.Value;
    }
    
    /// <summary>
    /// Gets the authenticated username.
    /// </summary>
    public static string? GetAuthenticatedUsername(this ControllerBase controller)
    {
        return controller.User.FindFirst(ClaimTypes.Name)?.Value
            ?? controller.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value
            ?? controller.User.FindFirst("username")?.Value;
    }
}