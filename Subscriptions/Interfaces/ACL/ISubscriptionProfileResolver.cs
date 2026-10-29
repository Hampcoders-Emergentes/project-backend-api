using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.Subscriptions.Interfaces.ACL;

public interface ISubscriptionProfileResolver
{
    Task<BusinessRole> ResolveBusinessRoleAsync(string userId);

    Task<string?> ResolveProfileFullNameAsync(string userId);

    /// <summary>
    /// ProfileId of this IAM user when its profile may use subscription endpoints (Active, or Suspended by a failed
    /// payment so the owner can pay); null otherwise (M3.31).
    /// </summary>
    Task<string?> ResolveSubscriptionProfileIdAsync(string userId);
}
