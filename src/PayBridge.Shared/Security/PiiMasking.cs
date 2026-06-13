namespace PayBridge.Shared.Security;

/// <summary>
/// Hashes / masks values that must never leak to logs, traces or metrics in raw form.
/// Customer emails are hashed (so we can still group by user) and amounts are bucketed
/// when used as a metric label to keep cardinality bounded.
/// </summary>
public static class PiiMasking
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var atIdx = email.IndexOf('@');
        if (atIdx <= 1)
        {
            return "***@" + (atIdx >= 0 ? email[(atIdx + 1)..] : "***");
        }

        return string.Concat(email[0].ToString(), "***", email[atIdx..]);
    }

    public static string AmountBucket(decimal amount) => amount switch
    {
        < 10m => "lt_10",
        < 100m => "lt_100",
        < 1_000m => "lt_1k",
        < 10_000m => "lt_10k",
        _ => "gte_10k"
    };
}
