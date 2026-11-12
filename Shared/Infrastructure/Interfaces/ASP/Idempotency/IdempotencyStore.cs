using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Idempotency;

public static class IdempotencyHeaders
{
    /// <summary>Optional request header with a client-generated unique key (UUID recommended, at most 128 characters).</summary>
    public const string Key = "Idempotency-Key";
    /// <summary>Response header set to "true" when the body is a replay of the first successful response.</summary>
    public const string Replayed = "Idempotent-Replayed";
}

/// <summary>The first successful response of a request, replayed verbatim on retries with the same key.</summary>
public sealed record IdempotentResponse(int StatusCode, object? Value, Type? DeclaredType, string? Location, string Fingerprint);

/// <summary>
///     Process-local store of idempotent responses (24 hours). Keys are scoped by caller and route, so two users can reuse
///     the same key. Running several API instances requires a shared cache instead (sticky sessions otherwise).
/// </summary>
public sealed class IdempotencyStore(IMemoryCache cache)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, string> _inFlight = new(StringComparer.Ordinal);

    public bool TryGet(string key, out IdempotentResponse? response) => cache.TryGetValue(CacheKey(key), out response);

    /// <summary>Claims the key for one request at a time; false when the same key is already being processed.</summary>
    public bool TryBegin(string key, string fingerprint) => _inFlight.TryAdd(key, fingerprint);

    public bool IsInFlightWithOtherPayload(string key, string fingerprint) =>
        _inFlight.TryGetValue(key, out var current) && !string.Equals(current, fingerprint, StringComparison.Ordinal);

    public void Complete(string key, IdempotentResponse? response)
    {
        if (response is not null)
            cache.Set(CacheKey(key), response, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention, Size = 1 });
        _inFlight.TryRemove(key, out _);
    }

    private static string CacheKey(string key) => "idempotency:" + key;
}

public static class IdempotencyServiceCollectionExtensions
{
    public static IServiceCollection AddIdempotentEndpoints(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IdempotencyStore>();
        return services;
    }
}
