using Electrolink.Backend.UnitTests.TestDoubles;
using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.IAM;

internal static class IamTestData
{
    public const string UserEmail = "user@electrolink.pe";

    public static User NewUser(AccessRole role = AccessRole.User)
    {
        var user = User.Create(Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects.Email.From(UserEmail), HashedPassword.FromHash("hash:secret"), role);
        user.ClearDomainEvents();
        return user;
    }

    /// <summary>Deterministic hashing double: hash(p) = "hash:" + p.</summary>
    public static Fake<IHashingService> Hashing() => new Fake<IHashingService>()
        .On(nameof(IHashingService.HashPassword), a => "hash:" + a[0])
        .On(nameof(IHashingService.VerifyPassword), a => "hash:" + a[0] == (string)a[1]!);

    /// <summary>Token double: refresh values are sequential and hashes are "h(" + value + ")".</summary>
    public static Fake<ITokenService> Tokens()
    {
        var counter = 0;
        return new Fake<ITokenService>()
            .On(nameof(ITokenService.GenerateToken), a => "jwt-" + ((User)a[0]!).Id.Value)
            .On(nameof(ITokenService.GenerateRefreshTokenValue), _ => "refresh-" + ++counter)
            .On(nameof(ITokenService.HashRefreshToken), a => "h(" + a[0] + ")")
            .Returns(nameof(ITokenService.RefreshTokenLifetime), TimeSpan.FromDays(7));
    }
}
