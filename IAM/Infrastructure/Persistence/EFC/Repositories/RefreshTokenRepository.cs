using Hampcoders.Electrolink.API.IAM.Domain.Model.Entities;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Persistence.EFC.Repositories;

public class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token) => await context.Set<RefreshToken>().AddAsync(token);

    public Task<RefreshToken?> FindByHashAsync(string tokenHash) =>
        context.Set<RefreshToken>().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserAsync(UserId userId, DateTime nowUtc) =>
        await context.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .ToListAsync();
}
