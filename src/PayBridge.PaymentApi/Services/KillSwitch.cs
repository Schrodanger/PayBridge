using StackExchange.Redis;

namespace PayBridge.PaymentApi.Services;

/// <summary>
/// Redis-backed kill switch. Operators flip "paybridge:kill:payments" to "1" to immediately
/// stop accepting new payments without a deploy / restart. Falls open (allows traffic) when
/// Redis is unavailable — we never want a cache outage to be a kill switch by accident.
/// </summary>
public interface IKillSwitch
{
    Task<bool> IsPaymentsDisabledAsync(CancellationToken ct);
}

public sealed class RedisKillSwitch : IKillSwitch
{
    public const string Key = "paybridge:kill:payments";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisKillSwitch> _logger;

    public RedisKillSwitch(IConnectionMultiplexer redis, ILogger<RedisKillSwitch> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> IsPaymentsDisabledAsync(CancellationToken ct)
    {
        try
        {
            var v = await _redis.GetDatabase().StringGetAsync(Key);
            return v == "1" || string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kill switch check failed, defaulting to enabled (fail-open)");
            return false;
        }
    }
}
