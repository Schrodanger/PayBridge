using PayBridge.Shared.Security;

namespace PayBridge.PaymentApi.Tests.Unit;

public class PiiMaskingTests
{
    [Theory]
    [InlineData("alice@example.com", "a***@example.com")]
    [InlineData("bob@x.io", "b***@x.io")]
    public void MaskEmail_keeps_first_char_and_domain(string input, string expected)
    {
        PiiMasking.MaskEmail(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("a@b.com", "***@b.com")]
    [InlineData("no-at-symbol", "***@***")]
    public void MaskEmail_handles_edge_cases(string? input, string expected)
    {
        PiiMasking.MaskEmail(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(5, "lt_10")]
    [InlineData(50, "lt_100")]
    [InlineData(500, "lt_1k")]
    [InlineData(5_000, "lt_10k")]
    [InlineData(50_000, "gte_10k")]
    public void AmountBucket_keeps_cardinality_bounded(decimal amount, string expected)
    {
        PiiMasking.AmountBucket(amount).Should().Be(expected);
    }
}
