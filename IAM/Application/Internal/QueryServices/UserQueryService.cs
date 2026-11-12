using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Repositories;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Hampcoders.Electrolink.API.IAM.Application.Internal.QueryServices;

/**
 * <summary>
 *     The user query service implementation class
 * </summary>
 * <remarks>
 *     This class is used to handle user queries
 * </remarks>
 */
public class UserQueryService(IUserRepository userRepository) : IUserQueryService
{
    /**
     * <summary>
     *     Handle get user by id query
     * </summary>
     * <param name="query">The query object containing the user id to search</param>
     * <returns>The user</returns>
     */
    public async Task<User?> Handle(GetUserByIdQuery query)
    {
        return await userRepository.FindByIdAsync(UserId.From(query.UserId));
    }

    /**
     * <summary>
     *     Handle get user by email query
     * </summary>
     * <param name="query">The query object for getting all users</param>
     * <returns>The user</returns>
     */
    public async Task<IEnumerable<User>> Handle(GetAllUsersQuery query)
    {
        return await userRepository.ListAsync(query.Page, query.PageSize);
    }

    public Task<int> Handle(GetUsersCountQuery query) => userRepository.CountAsync();

    /**
     * <summary>
     *     Handle get user by email query
     * </summary>
     * <param name="query">The query object containing the email to search</param>
     * <returns>The user</returns>
     */
    public async Task<User?> Handle(GetUserByEmailQuery query)
    {
        return await userRepository.FindByEmailAsync(query.Email);
    }
}
