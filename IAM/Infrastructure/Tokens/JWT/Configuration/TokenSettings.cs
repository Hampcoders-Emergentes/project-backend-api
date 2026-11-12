using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration;

/// <summary>JWT settings bound from the "TokenSettings" configuration section.</summary>
public class TokenSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "electrolink-api";
    public string Audience { get; set; } = "electrolink-clients";
    public int AccessTokenMinutes { get; set; } = 20;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Single source of truth for validating access tokens (ASP.NET pipeline and IAM middleware).</summary>
    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(Secret)),
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
}
