using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Electrolink.Backend.Characterization.Tests.Assets;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Aggregates;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.Commands;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Model.ValueObjects;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Repository;
using Hampcoders.Electrolink.API.Subscriptions.Domain.Services;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.PaymentGateway.StripeProvider;
using Hampcoders.Electrolink.API.Subscriptions.Interfaces.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Subscriptions;

public sealed class StripeRemediationTests
{
    private const string Secret = "whsec_synthetic";
    private const string DeletedEvent = """{"id":"evt_synthetic","object":"event","request":null,"type":"customer.subscription.deleted","data":{"object":{"id":"sub_synthetic","object":"subscription"}}}""";

    [Theory]
    [InlineData("19.99", 1999L)]
    [InlineData("0.10", 10L)]
    [InlineData("100", 10000L)]
    [InlineData("19.995", 2000L)]
    [InlineData("10.004", 1000L)]
    public void Amounts_are_sent_to_stripe_as_integer_cents(string amount, long cents)
        => Assert.Equal(cents, StripePaymentProvider.ToCents(decimal.Parse(amount, CultureInfo.InvariantCulture)));

    [Fact]
    public void Configured_prices_use_the_keys_the_resolver_reads()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(RepositoryRoot(), "appsettings.json")).Build();
        var resolver = new SubscriptionPlanPriceResolver(configuration);

        foreach (var role in new[] { EBusinessRole.Homeowner, EBusinessRole.Technician })
            foreach (var cycle in new[] { EBillingCycle.Monthly, EBillingCycle.Annual })
                Assert.StartsWith("price_", resolver.ResolvePriceIdForRole(role, cycle));

        Assert.Null(configuration["Stripe:Prices:HomeownerYearly"]);
        Assert.Null(configuration["Stripe:Prices:TechnicianYearly"]);
    }

    [Theory]
    [InlineData("ENTERPRISE_BASIC")]
    [InlineData("ENTERPRISE_PREMIUM")]
    public void Unset_enterprise_price_fails_instead_of_reaching_stripe_empty(string plan)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(RepositoryRoot(), "appsettings.json")).Build();
        var resolver = new SubscriptionPlanPriceResolver(configuration);

        Assert.Throws<InvalidOperationException>(() => resolver.ResolvePriceIdForRole(EBusinessRole.Company, EBillingCycle.Monthly, plan));
    }

    [Fact]
    public void Env_example_declares_every_price()
    {
        var template = File.ReadAllText(Path.Combine(RepositoryRoot(), ".env.example"));
        foreach (var plan in new[] { "Homeowner", "Technician", "EnterpriseBasic", "EnterprisePremium" })
            foreach (var cycle in new[] { "Monthly", "Annual" })
                Assert.Contains($"Stripe__Prices__{plan}{cycle}=", template);
    }

    [Fact]
    public async Task Signature_verification_returns_the_stripe_event_id_and_rejects_anything_else()
    {
        var provider = new StripePaymentProvider(Options.Create(new StripeSettings { SecretKey = "sk_test_synthetic", WebhookSecret = Secret }));

        var verified = await provider.ValidateWebhookSignatureAsync(DeletedEvent, Sign(DeletedEvent, Secret), Secret);
        Assert.Equal("evt_synthetic", verified.EventId);

        foreach (var signature in new[] { Sign(DeletedEvent, "whsec_other"), "malformed", "t=not-a-number,v1=00" })
            await Assert.ThrowsAsync<PaymentProviderException>(() => provider.ValidateWebhookSignatureAsync(DeletedEvent, signature, Secret));
    }

    [Theory]
    [InlineData("", "t=1,v1=00", 503)]
    [InlineData(Secret, "", 400)]
    public async Task Webhook_fails_closed_before_verification(string secret, string signature, int status)
    {
        var payments = AssignmentHttpFixture.Proxy<IPaymentProvider>((m, a) => throw new InvalidOperationException("Unexpected verification"));
        var controller = Controller(secret, signature, payments, Unexpected(), new RecordingInbox());

        var result = Assert.IsAssignableFrom<IStatusCodeActionResult>(await controller.Handle());

        Assert.Equal(status, result.StatusCode);
    }

    [Fact]
    public async Task Redelivered_event_runs_its_side_effects_once()
    {
        var degrades = 0;
        var commands = AssignmentHttpFixture.Proxy<ISubscriptionCommandService>((m, a) =>
        {
            Assert.IsType<DegradeSubscriptionCommand>(a![0]);
            degrades++;
            return Task.FromResult<Subscription>(null!);
        });
        var inbox = new RecordingInbox();

        foreach (var _ in new[] { 1, 2 })
            Assert.IsType<OkResult>(await Controller(Secret, "t=1,v1=00", VerifiedDeletedEvent(), commands, inbox).Handle());

        Assert.Equal(1, degrades);
        Assert.Equal(new[] { "evt_synthetic" }, inbox.Processed);
    }

    [Fact]
    public async Task Failed_handler_is_not_recorded_so_stripe_can_retry()
    {
        var commands = AssignmentHttpFixture.Proxy<ISubscriptionCommandService>((m, a) => throw new InvalidOperationException("Synthetic failure"));
        var inbox = new RecordingInbox();

        var result = Assert.IsType<StatusCodeResult>(await Controller(Secret, "t=1,v1=00", VerifiedDeletedEvent(), commands, inbox).Handle());

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Empty(inbox.Processed);
    }

    [Fact]
    public void Processed_event_table_matches_the_inbox_insert()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic", pg => pg.UseNetTopologySuite()).Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("sp_processed_webhook_events", script);
        foreach (var column in new[] { "event_id", "event_type", "received_at" })
            Assert.Contains(column, script);
    }

    private static StripeWebhookController Controller(
        string secret, string signature, IPaymentProvider payments, ISubscriptionCommandService commands, IWebhookEventInbox inbox)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Stripe:WebhookSecret"] = secret }).Build();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(DeletedEvent));
        context.Request.Headers["Stripe-Signature"] = signature;
        return new StripeWebhookController(configuration, NullLogger<StripeWebhookController>.Instance, commands, payments, inbox)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static IPaymentProvider VerifiedDeletedEvent() =>
        AssignmentHttpFixture.Proxy<IPaymentProvider>((m, a) => Task.FromResult(new WebhookEvent(
            "evt_synthetic", "customer.subscription.deleted", "sub_synthetic", null, null,
            """{"data":{"object":{"id":"sub_synthetic"}}}""")));

    private static ISubscriptionCommandService Unexpected() =>
        AssignmentHttpFixture.Proxy<ISubscriptionCommandService>((m, a) => throw new InvalidOperationException("Unexpected command"));

    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Hampcoders.Electrolink.API.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // Mirrors the contract of the real inbox; its SQL conflict handling needs PostgreSQL and is not exercised here.
    private sealed class RecordingInbox : IWebhookEventInbox
    {
        internal List<string> Processed { get; } = [];

        public async Task<bool> ProcessOnceAsync(string eventId, string eventType, Func<Task> handler)
        {
            if (Processed.Contains(eventId)) return false;
            await handler();
            Processed.Add(eventId);
            return true;
        }
    }
}
