using System.Collections.Concurrent;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Concurrency;

/// <summary>
///     Reports are generated synchronously in-process; a second request for the same report while the first is still
///     running (double click, client retry on a slow PDF) is refused with 409 REPORT_IN_PROGRESS instead of generating the
///     same file twice. Keys are scoped per caller and store, so one caller never learns about another's work.
///     In-memory: the guard covers one API instance.
/// </summary>
public sealed class ReportGenerationGuard
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.Ordinal);

    /// <returns>A lease to dispose when generation ends, or null when the same report is already being generated.</returns>
    public IDisposable? TryBegin(string reportKind, string actorId, string storeId)
    {
        var key = $"{reportKind}|{actorId}|{storeId}";
        return _running.TryAdd(key, 0) ? new Lease(_running, key) : null;
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> running, string key) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) running.TryRemove(key, out _);
        }
    }
}
