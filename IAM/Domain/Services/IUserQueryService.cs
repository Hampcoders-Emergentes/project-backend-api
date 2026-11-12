using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;

namespace Hampcoders.Electrolink.API.IAM.Domain.Services;

/**
 * <summary>
 *     The user query service interface
 * </summary>
 * <remarks>
 *     This service contract specifies handling behavior used to query users
 * </remarks>
 */
public interface IUserQueryService
{
    /**
     * <summary>
     *     Handle get user by id query
     * </summary>
     * <param name="query">The get user by id query</param>
     * <returns>The user if found, null otherwise</returns>
     */
    Task<User?> Handle(GetUserByIdQuery query);

    /**
     * <summary>
     *     Handle get all users query
     * </summary>
     * <param name="query">The get all users query</param>
     * <returns>The list of users</returns>
     */
    Task<IEnumerable<User>> Handle(GetAllUsersQuery query);

    /// <summary>Total number of users, for the paginated list.</summary>
    Task<int> Handle(GetUsersCountQuery query);
    
    /**
     * <summary>
     *     Handle get user by email query
     * </summary>
     * <param name="query">The get user by email query</param>
     * <returns>The user if found, null otherwise</returns>
     */
    Task<User?> Handle(GetUserByEmailQuery query);
}