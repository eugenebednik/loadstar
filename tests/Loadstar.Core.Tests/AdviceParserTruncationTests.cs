using Loadstar.Core.Ai;
using Xunit;

namespace Loadstar.Core.Tests;

/// <summary>
/// A truncated reply and a reply with no JSON at all are different failures with opposite fixes: raise
/// the token budget, versus fix the prompt. They used to report identically, which sent one
/// investigation at the prompt when a token ceiling was the cause.
/// </summary>
public sealed class AdviceParserTruncationTests
{
    [Fact]
    public void TruncatedObjectIsReportedAsTruncationNotAsMissingJson()
    {
        // Exactly the field failure: the model opened its object and ran out of budget mid-string.
        const string cut = """
            {"headline":"Поднять Мудрость до 100","screen":"Character","steps":[{"action":"Переરас
            """;

        var ex = Assert.Throws<AdviceParseException>(() => AdviceParser.Parse(cut, DateTimeOffset.Now));

        Assert.Contains("cut off", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);

        // And the raw reply travels with it, which is what makes it diagnosable.
        Assert.Equal(cut, ex.ResponseText);
    }

    [Fact]
    public void ReplyWithNoBraceAtAllIsReportedAsSuch()
    {
        var ex = Assert.Throws<AdviceParseException>(
            () => AdviceParser.Parse("I cannot help with that request.", DateTimeOffset.Now));

        Assert.Contains("no JSON object at all", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A complete object still parses, fenced or with prose around it.</summary>
    [Theory]
    [InlineData("{\"headline\":\"ok\"}")]
    [InlineData("Here you go:\n```json\n{\"headline\":\"ok\"}\n```\nHope that helps.")]
    [InlineData("{\"headline\":\"brace } inside a string\"}")]
    public void CompleteObjectsStillParse(string reply)
    {
        var advice = AdviceParser.Parse(reply, DateTimeOffset.Now);

        Assert.False(string.IsNullOrWhiteSpace(advice.Headline));
    }
}
