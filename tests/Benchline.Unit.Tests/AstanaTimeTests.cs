using System.Globalization;
using Benchline.Contracts;
using Xunit;

namespace Benchline.Unit.Tests;

public sealed class AstanaTimeTests
{
    [Theory]
    [InlineData("2026-12-31T18:59:00+00:00", "2026-12-31T23:59:00+05:00")]
    [InlineData("2026-12-31T19:00:00+00:00", "2027-01-01T00:00:00+05:00")]
    [InlineData("2026-07-01T12:30:00+03:00", "2026-07-01T14:30:00+05:00")]
    public void ConversionKeepsTheInstantAndUsesAstanaCalendarDates(string input, string expected)
    {
        var instant = DateTimeOffset.Parse(input, CultureInfo.InvariantCulture);
        var local = AstanaTime.Convert(instant);

        Assert.Equal(instant.UtcTicks, local.UtcTicks);
        Assert.Equal(TimeSpan.FromHours(5), local.Offset);
        Assert.Equal(expected, AstanaTime.Format(instant, "yyyy-MM-dd'T'HH:mm:sszzz"));
    }
}
