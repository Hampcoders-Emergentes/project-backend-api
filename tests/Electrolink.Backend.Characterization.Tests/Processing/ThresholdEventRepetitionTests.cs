using System.Text.Json;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.EventHandlers;
using Hampcoders.Electrolink.API.Processing.Domain.Model.ValueObjects;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Processing;

public sealed class ThresholdEventRepetitionTests
{
    [Theory]
    [InlineData("absent")]
    [InlineData("other-owner")]
    [InlineData("same-owner")]
    public async Task Equivalent_redelivery_repeats_calls_and_saves_even_when_threshold_values_do_not_change(string demo)
    {
        var h = new ThresholdEventScenario(demo);
        var evt = Event();
        var first = h.NewDelivery();
        await first.Handle(evt);

        Assert.Single(first.Commands);
        Assert.Equal(evt.OwnerId, first.Commands[0].OwnerId);
        Assert.Equal(Expected(), first.Memory[ThresholdEventScenario.OwnerDevice].CustomThresholds);
        Assert.Equal(Expected(), first.Memory[ThresholdEventScenario.SecondOwnerDevice].CustomThresholds);
        Assert.Equal(Expected(), h.Persisted[ThresholdEventScenario.OwnerDevice].Thresholds);
        Assert.Equal(Expected(), h.Persisted[ThresholdEventScenario.SecondOwnerDevice].Thresholds);
        Assert.Equal(ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.UnrelatedDevice].Thresholds);
        Assert.Equal(demo == "same-owner" ? 2 : 1, first.SaveAttempts.Count);
        Assert.Equal(first.SaveAttempts.Count, first.ConfirmedSaves);
        Assert.Equal(demo == "same-owner" ? 4 : 2, first.Updates.Count);
        Assert.Equal(demo == "same-owner" ? 3 : 2, first.EffectiveWrites);
        Assert.Equal(first.ExpectedSuccessfulTrace(), first.Trace);
        if (demo != "absent") Assert.Equal(demo == "same-owner" ? Expected() : ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.DemoDevice].Thresholds);
        if (demo == "other-owner") Assert.Equal(ThresholdEventScenario.OtherOwner, h.Persisted[ThresholdEventScenario.DemoDevice].Owner.ClientId);
        Assert.All(first.Memory.Values, stream => Assert.Empty(stream.DomainEvents));

        var confirmed = h.Persisted.ToDictionary(pair => pair.Key, pair => pair.Value);
        var replay = Replay(evt);
        var second = h.NewDelivery();
        Assert.NotSame(first.Memory[ThresholdEventScenario.OwnerDevice], second.Memory[ThresholdEventScenario.OwnerDevice]);
        Assert.Equal(confirmed[ThresholdEventScenario.OwnerDevice].Thresholds,
            second.Memory[ThresholdEventScenario.OwnerDevice].CustomThresholds);
        await second.Handle(replay);

        Assert.Equal(first.Commands, second.Commands);
        Assert.Equal(first.Trace, second.Trace);
        Assert.Equal(first.Updates.Count, second.Updates.Count);
        Assert.Equal(first.ConfirmedSaves, second.ConfirmedSaves);
        Assert.Equal(0, second.EffectiveWrites);
        Assert.Equal(confirmed.OrderBy(pair => pair.Key), h.Persisted.OrderBy(pair => pair.Key));
        Assert.All(second.Memory.Values, stream => Assert.Empty(stream.DomainEvents));
        Assert.Equal(JsonSerializer.Serialize(evt), JsonSerializer.Serialize(replay));
    }

    [Theory]
    [InlineData("lookup-owner")]
    [InlineData("lookup-demo")]
    [InlineData("update-demo")]
    [InlineData("save:1")]
    [InlineData("save:2")]
    public async Task Failure_interrupts_later_effects_and_fresh_redelivery_loads_only_confirmed_state(string point)
    {
        var h = new ThresholdEventScenario("same-owner");
        var evt = Event();
        var failure = new InvalidOperationException("synthetic " + point + " failure");
        var first = h.NewDelivery(point, failure);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => first.Handle(evt)));

        var ownerCommitted = point is "lookup-demo" or "update-demo" or "save:2";
        var ownerChangedInMemory = point != "lookup-owner";
        var demoChangedInMemory = ownerChangedInMemory;
        Assert.Single(first.Commands);
        Assert.Equal(ownerChangedInMemory ? Expected() : ThresholdConfig.Default(),
            first.Memory[ThresholdEventScenario.OwnerDevice].CustomThresholds);
        Assert.Equal(demoChangedInMemory ? Expected() : ThresholdConfig.Default(),
            first.Memory[ThresholdEventScenario.DemoDevice].CustomThresholds);
        Assert.Equal(ownerCommitted ? Expected() : ThresholdConfig.Default(),
            h.Persisted[ThresholdEventScenario.OwnerDevice].Thresholds);
        Assert.Equal(ownerCommitted ? Expected() : ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.DemoDevice].Thresholds);
        Assert.Equal(ownerCommitted ? 1 : 0, first.ConfirmedSaves);
        Assert.Equal(point == "lookup-owner" ? 0 : point == "save:2" ? 2 : 1, first.SaveAttempts.Count);
        Assert.Equal(point == "lookup-owner" ? 0 : point is "update-demo" or "save:2" ? 4 : 3, first.Updates.Count);
        var trace = first.ExpectedSuccessfulTrace();
        Assert.Equal(trace.Take((point == "update-demo" ? Array.LastIndexOf(trace, point) : Array.IndexOf(trace, point)) + 1), first.Trace);
        if (first.SaveAttempts.Count > 0)
            Assert.Equal(Expected(), first.SaveAttempts[0][ThresholdEventScenario.OwnerDevice].Thresholds);
        if (point == "save:2")
        {
            Assert.Equal(Expected(), first.SaveAttempts[1][ThresholdEventScenario.DemoDevice].Thresholds);
            Assert.Equal(Expected(), first.ConfirmedSnapshots[0][ThresholdEventScenario.DemoDevice].Thresholds);
        }

        var second = h.NewDelivery();
        Assert.NotSame(first.Memory[ThresholdEventScenario.OwnerDevice], second.Memory[ThresholdEventScenario.OwnerDevice]);
        Assert.NotSame(first.Memory[ThresholdEventScenario.DemoDevice], second.Memory[ThresholdEventScenario.DemoDevice]);
        Assert.Equal(ownerCommitted ? Expected() : ThresholdConfig.Default(),
            second.Memory[ThresholdEventScenario.OwnerDevice].CustomThresholds);
        Assert.Equal(ownerCommitted ? Expected() : ThresholdConfig.Default(), second.Memory[ThresholdEventScenario.DemoDevice].CustomThresholds);
        await second.Handle(Replay(evt));

        Assert.Single(second.Commands);
        Assert.Equal(first.Commands[0], second.Commands[0]);
        Assert.Equal(second.ExpectedSuccessfulTrace(), second.Trace);
        Assert.Equal(2, second.SaveAttempts.Count);
        Assert.Equal(2, second.ConfirmedSaves);
        Assert.Equal(4, second.Updates.Count);
        Assert.Equal(ownerCommitted ? 0 : 3, second.EffectiveWrites);
        Assert.Equal(Expected(), h.Persisted[ThresholdEventScenario.OwnerDevice].Thresholds);
        Assert.Equal(Expected(), h.Persisted[ThresholdEventScenario.SecondOwnerDevice].Thresholds);
        Assert.Equal(Expected(), h.Persisted[ThresholdEventScenario.DemoDevice].Thresholds);
        Assert.Equal(ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.UnrelatedDevice].Thresholds);
        Assert.All(second.Memory.Values, stream => Assert.Empty(stream.DomainEvents));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_owner_streams_complete_service_without_updating_foreign_demo(bool hasDemo)
    {
        var h = new ThresholdEventScenario(hasDemo ? "other-owner" : "absent", ownerStreams: false);
        var evt = Event();
        var first = h.NewDelivery();
        await first.Handle(evt);
        Assert.Single(first.Commands);
        Assert.Empty(first.Updates);
        Assert.Single(first.SaveAttempts);
        Assert.Equal(first.SaveAttempts.Count, first.ConfirmedSaves);
        Assert.Equal(0, first.EffectiveWrites);
        Assert.Equal(first.ExpectedSuccessfulTrace(), first.Trace);
        Assert.Equal(ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.UnrelatedDevice].Thresholds);
        if (hasDemo) Assert.Equal(ThresholdConfig.Default(), h.Persisted[ThresholdEventScenario.DemoDevice].Thresholds);
        var second = h.NewDelivery();
        await second.Handle(Replay(evt));
        Assert.Equal(first.Trace, second.Trace);
        Assert.Equal(first.Commands, second.Commands);
        Assert.Equal(first.Updates, second.Updates);
        Assert.Equal(0, second.EffectiveWrites);
    }

    private static ConsumptionThresholdsUpdatedIntegrationEvent Event() => new(
        ThresholdEventScenario.Owner,
        new Dictionary<string, decimal> { ["nominalVoltage"] = 230m, ["maxConsumptionWatts"] = 6400m,
            ["maxCurrentAmps"] = 28m, ["minPowerFactor"] = 0.9m, ["nominalFrequency"] = 50m,
            ["disconnectionThresholdMin"] = 7m, ["normalLimitAmps"] = 0.3m, ["alertLimitAmps"] = 0.8m },
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        { EventId = Guid.Parse("00000000-0000-0000-0000-000000000028") };

    private static ConsumptionThresholdsUpdatedIntegrationEvent Replay(ConsumptionThresholdsUpdatedIntegrationEvent evt)
    {
        var copy = JsonSerializer.Deserialize<ConsumptionThresholdsUpdatedIntegrationEvent>(JsonSerializer.Serialize(evt))!;
        Assert.NotSame(evt, copy);
        Assert.NotSame(evt.Thresholds, copy.Thresholds);
        Assert.Equal(evt.EventId, copy.EventId);
        Assert.Equal(evt.OccurredOn, copy.OccurredOn);
        Assert.Equal(evt.UpdatedAt, copy.UpdatedAt);
        Assert.Equal(evt.OwnerId, copy.OwnerId);
        Assert.Equal(evt.Thresholds.OrderBy(pair => pair.Key), copy.Thresholds.OrderBy(pair => pair.Key));
        return copy;
    }

    private static ThresholdConfig Expected() => ThresholdConfig.Create(230f, 6400f, 28f, 0.9f, 50f, 7, 0.3f, 0.8f);
}
