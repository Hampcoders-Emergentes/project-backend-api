using System.Text.Json;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;
using Xunit;

namespace Electrolink.Backend.Characterization.Tests.Shared;

// Observations of the current worker, including unsafe outcomes; not delivery requirements.
public sealed class OutboxProcessorTests
{
    [Fact]
    public async Task Success_publishes_deserialized_event_before_confirming_save()
    {
        var message = Message(1);
        message.Error = "previous synthetic error";
        using var h = new OutboxHarness(message);
        await h.RunBatch();
        var context = Assert.Single(h.Contexts);
        var published = Assert.IsType<SyntheticOutboxEvent>(Assert.Single(h.Delivered));
        Assert.Equal(Event(message), published);
        Assert.Equal(new[] { "publish:1", "save" }, h.Trace);
        Assert.Equal(1, context.SaveAttempts);
        Assert.Equal(1, context.ConfirmedSaves);
        Assert.False(context.SaveToken.IsCancellationRequested);
        Assert.NotSame(message, context.Rows[0]);
        Assert.NotSame(context.Rows[0], h.Persisted[0]);
        AssertProcessed(context.Rows[0], error: false);
        Assert.Equal(context.Rows[0].ProcessedOnUtc, context.SaveSnapshots[0][0].ProcessedOnUtc);
        Assert.Equal(context.Rows[0].ProcessedOnUtc, h.Persisted[0].ProcessedOnUtc);
        Assert.Null(h.Persisted[0].Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("json")]
    [InlineData("null")]
    [InlineData("contract")]
    public async Task Invalid_event_is_currently_marked_processed_and_next_message_is_delivered(string failure)
    {
        var bad = Message(1);
        if (failure == "type") bad.Type = "Missing.M26.Event, Missing.M26.Assembly";
        if (failure == "json") bad.Content = "{invalid synthetic json";
        if (failure == "null") bad.Content = "null";
        if (failure == "contract") { bad.Type = typeof(string).AssemblyQualifiedName!; bad.Content = "\"synthetic\""; }
        using var h = new OutboxHarness(bad, Message(2));
        await h.RunBatch();
        Assert.Equal(2, Assert.IsType<SyntheticOutboxEvent>(Assert.Single(h.Attempted)).Sequence);
        Assert.Single(h.Delivered);
        var context = Assert.Single(h.Contexts);
        AssertProcessed(context.Rows[0], error: true);
        AssertProcessed(context.Rows[1], error: false);
        Assert.Equal(1, context.SaveAttempts);
        Assert.Equal(1, context.ConfirmedSaves);
        Assert.Equal(context.Rows[0].Error, context.SaveSnapshots[0][0].Error);
        Assert.Equal(context.Rows[0].Error, h.Persisted[0].Error);
        Assert.All(h.Persisted, row => Assert.NotNull(row.ProcessedOnUtc));
        // Characterizes lost retry eligibility, not an accepted recovery policy.
        await h.RunBatch();
        Assert.Single(h.Attempted);
        Assert.Equal(0, h.Contexts[1].SaveAttempts);
    }

    [Fact]
    public async Task Publisher_failure_is_swallowed_saved_as_processed_and_does_not_stop_next_event()
    {
        using var h = new OutboxHarness(Message(1), Message(2));
        var failure = new InvalidOperationException("synthetic publisher failure");
        h.OnPublish = (evt, _) => evt.Sequence == 1 ? Task.FromException(failure) : Task.CompletedTask;
        await h.RunBatch();
        Assert.Equal(new[] { 1, 2 }, h.Attempted.Cast<SyntheticOutboxEvent>().Select(e => e.Sequence));
        Assert.Equal(2, Assert.IsType<SyntheticOutboxEvent>(Assert.Single(h.Delivered)).Sequence);
        var context = Assert.Single(h.Contexts);
        AssertProcessed(context.Rows[0], error: true);
        Assert.Equal(failure.Message, context.Rows[0].Error);
        AssertProcessed(context.Rows[1], error: false);
        Assert.Equal(new[] { "publish:1", "publish:2", "save" }, h.Trace);
        Assert.Equal(1, context.SaveAttempts);
        Assert.Equal(1, context.ConfirmedSaves);
        Assert.Equal(failure.Message, context.SaveSnapshots[0][0].Error);
        Assert.Equal(failure.Message, h.Persisted[0].Error);
        await h.RunBatch();
        Assert.Equal(2, h.Attempted.Count);
        Assert.Equal(0, h.Contexts[1].SaveAttempts);
    }

    [Fact]
    public async Task Cancellation_before_query_escapes_without_publication_or_save()
    {
        using var h = new OutboxHarness(Message(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunBatch(cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Empty(h.Attempted);
        Assert.Empty(h.Delivered);
        var context = Assert.Single(h.Contexts);
        Assert.Equal(0, context.SaveAttempts);
        Assert.Equal(0, context.ConfirmedSaves);
        AssertPending(context.Rows[0]);
        AssertPending(h.Persisted[0]);
    }

    [Fact]
    public async Task Successful_publication_after_token_cancellation_returns_before_save_when_another_message_remains()
    {
        using var h = new OutboxHarness(Message(1), Message(2));
        using var cts = new CancellationTokenSource();
        h.OnPublish = (_, token) =>
        {
            Assert.Equal(cts.Token, token);
            cts.Cancel();
            return Task.CompletedTask;
        };
        // Successful publication with a canceled token remains outside the M2.7 correction.
        await h.RunBatch(cts.Token);
        Assert.Single(h.Attempted);
        Assert.Single(h.Delivered);
        var context = Assert.Single(h.Contexts);
        AssertProcessed(context.Rows[0], error: false);
        AssertPending(context.Rows[1]);
        Assert.Equal(0, context.SaveAttempts);
        Assert.Equal(0, context.ConfirmedSaves);
        Assert.Empty(context.SaveSnapshots);
        Assert.All(h.Persisted, AssertPending);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Requested_publisher_cancellation_propagates_original_without_marks_or_save(
        bool anotherMessageRemains, bool taskCanceledException)
    {
        var canceledMessage = Message(1);
        canceledMessage.Error = "previous synthetic error";
        using var h = new OutboxHarness(anotherMessageRemains
            ? new[] { canceledMessage, Message(2) } : new[] { canceledMessage });
        using var cts = new CancellationTokenSource();
        var cause = new InvalidOperationException("synthetic cancellation cause");
        OperationCanceledException failure = taskCanceledException
            ? new TaskCanceledException("synthetic requested stop", cause, cts.Token)
            : new OperationCanceledException("synthetic requested stop", cause, cts.Token);
        h.OnPublish = (_, token) =>
        {
            Assert.Equal(cts.Token, token);
            cts.Cancel();
            return Task.FromException(failure);
        };

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunBatch(cts.Token));

        Assert.Same(failure, error);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(1, Assert.IsType<SyntheticOutboxEvent>(Assert.Single(h.Attempted)).Sequence);
        Assert.Empty(h.Delivered);
        Assert.Equal(new[] { "publish:1" }, h.Trace);
        var context = Assert.Single(h.Contexts);
        Assert.Equal(canceledMessage.ProcessedOnUtc, context.Rows[0].ProcessedOnUtc);
        Assert.Equal(canceledMessage.Error, context.Rows[0].Error);
        Assert.Equal(0, context.SaveAttempts);
        Assert.Equal(0, context.ConfirmedSaves);
        Assert.Empty(context.SaveSnapshots);
        Assert.Equal(canceledMessage.ProcessedOnUtc, h.Persisted[0].ProcessedOnUtc);
        Assert.Equal(canceledMessage.Error, h.Persisted[0].Error);
        if (anotherMessageRemains)
        {
            AssertPending(context.Rows[1]);
            AssertPending(h.Persisted[1]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publisher_cancellation_without_requested_stop_keeps_current_ordinary_failure_treatment(
        bool anotherMessageRemains)
    {
        using var h = new OutboxHarness(anotherMessageRemains
            ? new[] { Message(1), Message(2) } : new[] { Message(1) });
        using var stopping = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var failure = new OperationCanceledException("synthetic unrelated cancellation", unrelated.Token);
        h.OnPublish = (evt, token) =>
        {
            Assert.Equal(stopping.Token, token);
            Assert.False(token.IsCancellationRequested);
            return evt.Sequence == 1 ? Task.FromException(failure) : Task.CompletedTask;
        };

        // Still an ordinary-error observation, not an accepted retry/recovery policy.
        await h.RunBatch(stopping.Token);

        Assert.False(stopping.IsCancellationRequested);
        Assert.Equal(anotherMessageRemains ? new[] { 1, 2 } : new[] { 1 },
            h.Attempted.Cast<SyntheticOutboxEvent>().Select(evt => evt.Sequence));
        Assert.Equal(anotherMessageRemains ? new[] { 2 } : Array.Empty<int>(),
            h.Delivered.Cast<SyntheticOutboxEvent>().Select(evt => evt.Sequence));
        var context = Assert.Single(h.Contexts);
        AssertProcessed(context.Rows[0], error: true);
        Assert.Equal(failure.Message, context.Rows[0].Error);
        Assert.Equal(1, context.SaveAttempts);
        Assert.Equal(1, context.ConfirmedSaves);
        Assert.Equal(stopping.Token, context.SaveToken);
        Assert.Equal(failure.Message, context.SaveSnapshots[0][0].Error);
        Assert.Equal(failure.Message, h.Persisted[0].Error);
        Assert.All(h.Persisted, row => Assert.NotNull(row.ProcessedOnUtc));
        if (anotherMessageRemains) AssertProcessed(context.Rows[1], error: false);
    }

    [Fact]
    public async Task Save_failure_after_delivery_escapes_and_fresh_context_can_deliver_again()
    {
        using var h = new OutboxHarness(Message(1));
        var failure = new InvalidOperationException("synthetic save failure");
        h.NextSaveFailure = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunBatch()));
        var first = Assert.Single(h.Contexts);
        Assert.Single(h.Delivered);
        AssertProcessed(first.Rows[0], error: false);
        AssertProcessed(first.SaveSnapshots[0][0], error: false);
        Assert.Equal(1, first.SaveAttempts);
        Assert.Equal(0, first.ConfirmedSaves);
        AssertPending(h.Persisted[0]);

        await h.RunBatch();
        var second = h.Contexts[1];
        Assert.NotSame(first, second);
        Assert.NotSame(first.Rows[0], second.Rows[0]);
        AssertPending(second.LoadedSnapshot[0]);
        Assert.Equal(2, h.Delivered.Count);
        Assert.Equal(h.Delivered[0], h.Delivered[1]);
        Assert.NotSame(h.Delivered[0], h.Delivered[1]);
        Assert.Equal(1, second.SaveAttempts);
        Assert.Equal(1, second.ConfirmedSaves);
        AssertProcessed(second.Rows[0], error: false);
        Assert.Equal(second.Rows[0].ProcessedOnUtc, h.Persisted[0].ProcessedOnUtc);
        Assert.Equal(new[] { "publish:1", "save", "publish:1", "save" }, h.Trace);
        await h.RunBatch();
        Assert.Equal(2, h.Delivered.Count);
        Assert.Equal(0, h.Contexts[2].SaveAttempts);
    }

    [Fact]
    public async Task Selection_excludes_processed_orders_pending_and_limits_each_batch_to_100()
    {
        var processed = Message(0);
        processed.ProcessedOnUtc = processed.OccurredOnUtc;
        processed.Error = "preserved synthetic error";
        using var h = new OutboxHarness(Enumerable.Range(1, 101).Reverse().Select(Message).Append(processed).ToArray());
        await h.RunBatch();
        Assert.Equal(Enumerable.Range(1, 100), h.Attempted.Cast<SyntheticOutboxEvent>().Select(e => e.Sequence));
        Assert.Equal(100, h.Delivered.Count);
        var context = Assert.Single(h.Contexts);
        Assert.Equal(1, context.SaveAttempts);
        Assert.Equal(1, context.ConfirmedSaves);
        Assert.Equal(100, context.SaveSnapshots[0].Count(row => row.ProcessedOnUtc != null && row.Error == null));
        AssertPending(h.Persisted.Single(row => row.Id == Message(101).Id));
        var untouched = h.Persisted.Single(row => row.Id == processed.Id);
        Assert.Equal(processed.ProcessedOnUtc, untouched.ProcessedOnUtc);
        Assert.Equal(processed.Error, untouched.Error);
        await h.RunBatch();
        Assert.Equal(Enumerable.Range(1, 101), h.Delivered.Cast<SyntheticOutboxEvent>().Select(e => e.Sequence));
        Assert.Equal(1, h.Contexts[1].ConfirmedSaves);
        Assert.All(h.Persisted, row => Assert.NotNull(row.ProcessedOnUtc));
    }

    [Fact]
    public async Task Empty_batch_does_not_publish_or_attempt_save()
    {
        using var h = new OutboxHarness();
        await h.RunBatch();
        Assert.Empty(h.Attempted);
        Assert.Empty(h.Delivered);
        var context = Assert.Single(h.Contexts);
        Assert.Empty(context.Rows);
        Assert.Equal(0, context.SaveAttempts);
        Assert.Equal(0, context.ConfirmedSaves);
        Assert.Empty(h.Persisted);
    }

    private static void AssertPending(OutboxMessage row)
    {
        Assert.Null(row.ProcessedOnUtc);
        Assert.Null(row.Error);
    }

    private static void AssertProcessed(OutboxMessage row, bool error)
    {
        Assert.NotNull(row.ProcessedOnUtc);
        if (error) Assert.False(string.IsNullOrWhiteSpace(row.Error));
        else Assert.Null(row.Error);
    }

    private static SyntheticOutboxEvent Event(OutboxMessage message) =>
        JsonSerializer.Deserialize<SyntheticOutboxEvent>(message.Content)!;

    private static OutboxMessage Message(int sequence)
    {
        var evt = new SyntheticOutboxEvent(Guid.Parse($"00000000-0000-0000-0000-{sequence:D12}"),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(sequence), sequence);
        return new OutboxMessage { Id = evt.EventId, OccurredOnUtc = evt.OccurredOn,
            Type = typeof(SyntheticOutboxEvent).AssemblyQualifiedName!, Content = JsonSerializer.Serialize(evt) };
    }
}
