using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Hampcoders.Electrolink.API.Shared.Domain.Model.Events;
using Hampcoders.Electrolink.API.Shared.Infrastructure.BackgroundServices;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Electrolink.Backend.Characterization.Tests.Shared;

public sealed record SyntheticOutboxEvent(Guid EventId, DateTime OccurredOn, int Sequence) : IIntegrationEvent;

internal sealed class OutboxHarness : IDisposable
{
    private readonly ServiceProvider services;
    private readonly OutboxProcessorBackgroundService worker;
    private static readonly MethodInfo BatchMethod = typeof(OutboxProcessorBackgroundService)
        .GetMethod("ProcessOutboxMessagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Existing outbox batch method not found.");

    internal List<OutboxMessage> Persisted { get; private set; }
    internal List<OutboxContextDouble> Contexts { get; } = new();
    internal List<IIntegrationEvent> Attempted { get; } = new();
    internal List<IIntegrationEvent> Delivered { get; } = new();
    internal List<string> Trace { get; } = new();
    internal Func<SyntheticOutboxEvent, CancellationToken, Task> OnPublish { get; set; } = (_, _) => Task.CompletedTask;
    internal Exception? NextSaveFailure { get; set; }

    internal OutboxHarness(params OutboxMessage[] rows)
    {
        Persisted = Copy(rows);
        var mediator = DispatchProxy.Create<IMediator, OutboxMediatorDouble>();
        ((OutboxMediatorDouble)(object)mediator).Publish = Publish;
        var registrations = new ServiceCollection();
        registrations.AddScoped<AppDbContext>(_ =>
        {
            var context = new OutboxContextDouble(this, Persisted, NextSaveFailure);
            NextSaveFailure = null;
            Contexts.Add(context);
            return context;
        });
        registrations.AddSingleton(mediator);
        services = registrations.BuildServiceProvider();
        worker = new OutboxProcessorBackgroundService(NullLogger<OutboxProcessorBackgroundService>.Instance, services);
    }

    // Invoke the existing private batch only; never StartAsync or ExecuteAsync.
    internal Task RunBatch(CancellationToken token = default) =>
        (Task)(BatchMethod.Invoke(worker, new object[] { token })
            ?? throw new InvalidOperationException("Outbox batch returned no task."));

    private async Task Publish(IIntegrationEvent evt, CancellationToken token)
    {
        if (evt is not SyntheticOutboxEvent synthetic) throw new InvalidOperationException("Unexpected event contract.");
        Trace.Add("publish:" + synthetic.Sequence);
        Attempted.Add(evt);
        await OnPublish(synthetic, token);
        Delivered.Add(evt);
    }

    internal void Commit(IEnumerable<OutboxMessage> rows) => Persisted = Copy(rows);
    internal static List<OutboxMessage> Copy(IEnumerable<OutboxMessage> rows) => rows.Select(row => new OutboxMessage
    {
        Id = row.Id, Type = row.Type, Content = row.Content, OccurredOnUtc = row.OccurredOnUtc,
        ProcessedOnUtc = row.ProcessedOnUtc, Error = row.Error
    }).ToList();

    public void Dispose()
    {
        worker.Dispose();
        services.Dispose();
    }
}

internal sealed class OutboxContextDouble : AppDbContext
{
    private readonly OutboxHarness store;
    private readonly Exception? saveFailure;
    internal List<OutboxMessage> Rows { get; }
    internal List<OutboxMessage> LoadedSnapshot { get; }
    internal List<List<OutboxMessage>> SaveSnapshots { get; } = new();
    internal int SaveAttempts { get; private set; }
    internal int ConfirmedSaves { get; private set; }
    internal CancellationToken SaveToken { get; private set; }

    internal OutboxContextDouble(OutboxHarness store, IEnumerable<OutboxMessage> rows, Exception? saveFailure)
        : base(new DbContextOptionsBuilder<AppDbContext>().Options)
    {
        this.store = store;
        this.saveFailure = saveFailure;
        Rows = OutboxHarness.Copy(rows);
        LoadedSnapshot = OutboxHarness.Copy(Rows);
        OutboxMessages = new OutboxSetDouble(Rows);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveAttempts++;
        SaveToken = cancellationToken;
        SaveSnapshots.Add(OutboxHarness.Copy(Rows));
        store.Trace.Add("save");
        cancellationToken.ThrowIfCancellationRequested();
        if (saveFailure != null) return Task.FromException<int>(saveFailure);
        store.Commit(Rows);
        ConfirmedSaves++;
        return Task.FromResult(Rows.Count);
    }
}

// LINQ-to-Objects executes the worker's own Where/OrderBy/Take expression.
// Only async enumeration is supplied here: no SQL translation or database is simulated.
internal sealed class OutboxSetDouble : DbSet<OutboxMessage>, IQueryable<OutboxMessage>, IAsyncEnumerable<OutboxMessage>
{
    public override Microsoft.EntityFrameworkCore.Metadata.IEntityType EntityType =>
        throw new NotSupportedException("Model access is outside this persistence double.");
    private readonly IQueryable<OutboxMessage> query;
    internal OutboxSetDouble(IEnumerable<OutboxMessage> rows) => query = new OutboxAsyncQuery<OutboxMessage>(rows);
    Type IQueryable.ElementType => query.ElementType;
    Expression IQueryable.Expression => query.Expression;
    IQueryProvider IQueryable.Provider => query.Provider;
    IEnumerator<OutboxMessage> IEnumerable<OutboxMessage>.GetEnumerator() => query.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => query.GetEnumerator();
    public override IAsyncEnumerator<OutboxMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        ((IAsyncEnumerable<OutboxMessage>)query).GetAsyncEnumerator(cancellationToken);
}

internal sealed class OutboxAsyncQuery<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    internal OutboxAsyncQuery(IEnumerable<T> rows) : base(rows) { }
    internal OutboxAsyncQuery(Expression expression) : base(expression) { }
    IQueryProvider IQueryable.Provider => new OutboxQueryProvider();
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new OutboxAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator(), cancellationToken);
}

internal sealed class OutboxQueryProvider : IQueryProvider
{
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new OutboxAsyncQuery<TElement>(expression);
    public IQueryable CreateQuery(Expression expression) => throw new NotSupportedException("Only typed batch queries are expected.");
    public object? Execute(Expression expression) => throw new NotSupportedException("Only async enumeration is expected.");
    public TResult Execute<TResult>(Expression expression) => throw new NotSupportedException("Only async enumeration is expected.");
}

internal sealed class OutboxAsyncEnumerator<T>(IEnumerator<T> inner, CancellationToken token) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync()
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(inner.MoveNext());
    }
    public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
}

// Public/non-sealed solely for DispatchProxy's generated subclass, as in existing doubles.
public class OutboxMediatorDouble : DispatchProxy
{
    internal Func<IIntegrationEvent, CancellationToken, Task> Publish { get; set; } =
        (_, _) => throw new InvalidOperationException("Publisher double not configured.");
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "Publish" && args is [IIntegrationEvent evt, CancellationToken token])
            return Publish(evt, token);
        throw new InvalidOperationException("Unexpected mediator access: " + targetMethod?.Name);
    }
}
