using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;


namespace Hampcoders.Electrolink.API.IAM.Infrastructure.Persistence.EFC.Repositories;

/**
 * <summary>
 *     The user repository
 * </summary>
 * <remarks>
 *     This repository is used to manage users
 * </remarks>
 */
public class UserRepository(AppDbContext context) : BaseRepository<User, UserId>(context), IUserRepository
{
    /**
     * <summary>
     *     Find a user by username
     * </summary>
     * <param name="email">The username to search</param>
     * <returns>The user</returns>
     */
    public async Task<User?> FindByEmailAsync(string email)
    {
        return await Context.Set<User>().FirstOrDefaultAsync(u => u.Email.Value == email);
    }

    /**
     * <summary>
     *     Check if a user exists by username
     * </summary>
     * <param name="email">The username to search</param>
     * <returns>True if the user exists, false otherwise</returns>
     */
    public async Task<bool> ExistsByEmail(string email)
    {
        return await Context.Set<User>().AsNoTracking().AnyAsync(u => u.Email.Value == email);
    }

    public async Task<IEnumerable<User>> ListAsync(int page, int pageSize)
    {
        return await Context.Set<User>()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public Task<int> CountAsync() => Context.Set<User>().CountAsync();

    public async Task<User?> FindByPasswordResetTokenAsync(string resetTokenHash)
    {
        return await Context.Set<User>()
            .FirstOrDefaultAsync(u =>
                u.PasswordResetToken != null && u.PasswordResetToken.Value == resetTokenHash);
    }
}