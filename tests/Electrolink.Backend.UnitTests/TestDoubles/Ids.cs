using Hampcoders.Electrolink.API.Shared.Domain.Model.ValueObjects;

namespace Electrolink.Backend.UnitTests.TestDoubles;

/// <summary>Well-formed identifiers reused by tests. Values are immutable, so sharing them keeps tests independent.</summary>
public static class Ids
{
    public const string User = "us-11111111-1111-1111-1111-111111111111";
    public const string OtherUser = "us-22222222-2222-2222-2222-222222222222";
    public const string Technician = "tech-11111111-1111-1111-1111-111111111111";
    public const string Store = "store-1";

    public static UserId Actor => UserId.From(User);
}
