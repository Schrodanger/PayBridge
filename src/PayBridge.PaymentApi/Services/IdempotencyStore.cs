using System.Text.Json;
using PayBridge.PaymentApi.Contracts;
using StackExchange.Redis;

namespace PayBridge.PaymentApi.Services;

/// <summary>
/// Stores the canonical response for a given (merchantId, idempotencyKey) pair so duplicate
/// requests are served from cache rather than re-processed. The DB unique index is the source
/// of truth — this cache is just a performance optimization to avoid re-running the pipeline.
/// </summary>
public interface IIdempotencyStore
{
    Task<PaymentResponse?> TryGetAsync(string merchantId, string idempotencyKey, CancellationToken ct);
    Task SaveAsync(string merchantId, string idempotencyKey, PaymentResponse response, CancellationToken ct);
}

public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public RedisIdempotencyStore(IConnectionMultiplexer redis, ILogger<RedisIdempotencyStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    private static string Key(string merchantId, string idempotencyKey) =>
        $"paybridge:idem:{merchantId}:{idempotencyKey}";

    public async Task<PaymentResponse?> TryGetAsync(string merchantId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var v = await _redis.GetDatabase().StringGetAsync(Key(merchantId, idempotencyKey));
            if (v.IsNullOrEmpty)
            {
                return null;
            }
            return JsonSerializer.Deserialize<PaymentResponse>(v.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency cache read failed; falling through to DB lookup");
            return null;
        }
    }

    public async Task SaveAsync(string merchantId, string idempotencyKey, PaymentResponse response, CancellationToken ct)
    {
        try
        {
            await _redis.GetDatabase().StringSetAsync(
                Key(merchantId, idempotencyKey),
                JsonSerializer.Serialize(response),
                Ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency cache write failed; subsequent retries will hit DB");
        }
    }
}
