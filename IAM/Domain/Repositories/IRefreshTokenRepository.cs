using Hampcoders.Electrolink.API.IAM.Domain.Model.Entities;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.IAM.Domain.Repositories;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token);
    Task<RefreshToken?> FindByHashAsync(string tokenHash);
    Task<IReadOnlyList<RefreshToken>> ListActiveByUserAsync(UserId userId, DateTime nowUtc);
}
