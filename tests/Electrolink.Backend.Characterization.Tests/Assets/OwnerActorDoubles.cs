using Hampcoders.Electrolink.API.IAM.Application.Internal.OutboundServices;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.IAM.Domain.Model.Queries;
using Hampcoders.Electrolink.API.IAM.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.IAM.Domain.Services;
using Hampcoders.Electrolink.API.Profiles.Interfaces.ACL;
using Microsoft.Extensions.DependencyInjection;
namespace Electrolink.Backend.Characterization.Tests.Assets;

// Synthetic IAM token/user and Profiles doubles shared by the owner-route tests (M3.22, M3.23).
// Tokens are the dictionary keys: victim, attacker, company, suspended, no-profile, inactive-profile, technician, subject-mismatch, admin,
// and for technician routes (M3.24): tech-owner, tech-other, tech-suspended, tech-inactive, tech-mismatch;
// catalogue administrators (M3.25): superadmin, admin-suspended (no profiles);
// subscriptions (M3.31): payment-suspended (HomeOwner profile Suspended, as left by a failed payment).
internal sealed class OwnerActorDoubles
{
    internal const string Victim = "ho-victim-m322";
    internal const string Attacker = "ho-attacker-m322";
    internal const string Company = "comp-owner-m322";
    internal const string TechOwner = "tech-owner-m324";
    internal const string TechOther = "tech-other-m324";
    private readonly ITokenService tokens;
    private readonly IUserQueryService userQueries;
    private readonly IProfilesContextFacade profiles;

    internal OwnerActorDoubles()
    {
        User Make(string name, AccessRole role = AccessRole.User) => User.Create(Email.From("m322-" + name + "@example.invalid"), HashedPassword.FromHash("synthetic"), role);
        var users = new Dictionary<string, User>
        {
            ["victim"] = Make("victim"), ["attacker"] = Make("attacker"), ["company"] = Make("company"), ["suspended"] = Make("suspended"),
            ["no-profile"] = Make("no-profile"), ["inactive-profile"] = Make("inactive"), ["technician"] = Make("technician"),
            ["subject-mismatch"] = Make("mismatch"), ["admin"] = Make("admin", AccessRole.Admin),
            ["tech-owner"] = Make("tech-owner"), ["tech-other"] = Make("tech-other"), ["tech-suspended"] = Make("tech-suspended"),
            ["tech-inactive"] = Make("tech-inactive"), ["tech-mismatch"] = Make("tech-mismatch"),
            ["superadmin"] = Make("superadmin", AccessRole.SuperAdmin), ["admin-suspended"] = Make("admin-suspended", AccessRole.Admin),
            ["payment-suspended"] = Make("payment-suspended")
        };
        users["admin-suspended"].Suspend("synthetic");
        users["tech-suspended"].Suspend("synthetic");
        users["suspended"].Suspend("synthetic");
        // Profile of each user: (status, role, subject). The suspended user keeps a valid, concordant profile of its own.
        var claims = new Dictionary<string, (string Status, string Role, string Subject)?>
        {
            ["victim"] = ("Active", "HomeOwner", Victim), ["attacker"] = ("Active", "HomeOwner", Attacker), ["company"] = ("Active", "Company", Company),
            ["suspended"] = ("Active", "HomeOwner", "ho-suspended-m322"), ["no-profile"] = null, ["inactive-profile"] = ("Deactivated", "HomeOwner", Victim),
            ["technician"] = ("Active", "Technician", "tech-m322"), ["subject-mismatch"] = ("Active", "HomeOwner", Victim), ["admin"] = null,
            ["tech-owner"] = ("Active", "Technician", TechOwner), ["tech-other"] = ("Active", "Technician", TechOther),
            ["tech-suspended"] = ("Active", "Technician", "tech-suspended-m324"), ["tech-inactive"] = ("Deactivated", "Technician", TechOwner),
            ["tech-mismatch"] = ("Active", "Technician", TechOwner), ["superadmin"] = null, ["admin-suspended"] = null,
            ["payment-suspended"] = ("Suspended", "HomeOwner", "ho-payment-m331")
        };
        // Technician subject that Profiles actually links to each user; "tech-mismatch" claims the owner's subject from elsewhere.
        var technicianOfUser = new Dictionary<string, string>
        {
            ["technician"] = "tech-m322", ["tech-owner"] = TechOwner, ["tech-other"] = TechOther, ["tech-suspended"] = "tech-suspended-m324",
            ["tech-inactive"] = TechOwner, ["tech-mismatch"] = "tech-elsewhere-m324"
        };
        var tokens = AssignmentHttpFixture.Proxy<ITokenService>((m, a) =>
            Task.FromResult(users.TryGetValue((string)a![0]!, out var u) ? u.Id.Value : null));
        var userQueries = AssignmentHttpFixture.Proxy<IUserQueryService>((m, a) =>
            a![0] is GetUserByIdQuery q ? Task.FromResult<User?>(users.Values.SingleOrDefault(u => u.Id.Value == q.UserId)) : throw new InvalidOperationException("Unexpected user call"));
        string? KeyOf(string userId) => users.SingleOrDefault(p => p.Value.Id.Value == userId).Key;
        var profiles = AssignmentHttpFixture.Proxy<IProfilesContextFacade>((m, a) =>
        {
            var arg = (string)a![0]!;
            return m.Name switch
            {
                nameof(IProfilesContextFacade.GetProfileClaimsAsync) => Task.FromResult<(string ProfileId, string ProfileStatus, string? BusinessRole, string? RoleSubjectId, string? SubscriptionTier)?>(
                    KeyOf(arg) is { } key && claims[key] is { } c ? ("prof-" + key, c.Status, c.Role, c.Subject, null) : null),
                // The real owner of each subject's profile; "subject-mismatch" claims the victim's subject from another profile.
                nameof(IProfilesContextFacade.GetProfileIdByHomeownerIdAsync) => Task.FromResult<string?>(arg switch { Victim => "prof-victim", Attacker => "prof-attacker", "ho-suspended-m322" => "prof-suspended", _ => null }),
                nameof(IProfilesContextFacade.GetTechnicianIdByUserIdAsync) =>
                    Task.FromResult(KeyOf(arg) is { } tech && technicianOfUser.TryGetValue(tech, out var subject) ? subject : null),
                nameof(IProfilesContextFacade.GetProfileIdByCompanyIdAsync) => Task.FromResult<string?>(arg == Company ? "prof-company" : null),
                _ => throw new InvalidOperationException("Unexpected profile call " + m.Name)
            };
        });
        this.tokens = tokens; this.userQueries = userQueries; this.profiles = profiles;
    }

    internal void AddTo(IServiceCollection services)
    {
        services.AddSingleton(tokens); services.AddSingleton(userQueries); services.AddSingleton(profiles);
    }
}
