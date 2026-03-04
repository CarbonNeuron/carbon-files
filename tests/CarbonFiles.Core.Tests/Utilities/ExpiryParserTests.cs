using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Utilities;

public class ExpiryParserTests
{
    [Theory]
    [InlineData("1m", 1)]
    [InlineData("15m", 15)]
    [InlineData("45m", 45)]
    public void Parse_DurationMinutes_ReturnsCorrectTime(string input, int expectedMinutes)
    {
        var before = DateTime.UtcNow.AddMinutes(expectedMinutes);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddMinutes(expectedMinutes);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("2h", 2)]
    [InlineData("6h", 6)]
    [InlineData("12h", 12)]
    [InlineData("48h", 48)]
    public void Parse_DurationHours_ReturnsCorrectTime(string input, int expectedHours)
    {
        var before = DateTime.UtcNow.AddHours(expectedHours);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddHours(expectedHours);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Theory]
    [InlineData("1d", 1)]
    [InlineData("3d", 3)]
    [InlineData("7d", 7)]
    [InlineData("14d", 14)]
    [InlineData("30d", 30)]
    public void Parse_DurationDays_ReturnsCorrectTime(string input, int expectedDays)
    {
        var before = DateTime.UtcNow.AddDays(expectedDays);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddDays(expectedDays);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Theory]
    [InlineData("1w", 7)]
    [InlineData("2w", 14)]
    [InlineData("4w", 28)]
    public void Parse_DurationWeeks_ReturnsCorrectTime(string input, int expectedDays)
    {
        var before = DateTime.UtcNow.AddDays(expectedDays);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddDays(expectedDays);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("90s", 90)]
    [InlineData("3600s", 3600)]
    public void Parse_DurationSeconds_ReturnsCorrectTime(string input, int expectedSeconds)
    {
        var before = DateTime.UtcNow.AddSeconds(expectedSeconds);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddSeconds(expectedSeconds);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public void Parse_Never_ReturnsNull()
    {
        var result = ExpiryParser.Parse("never");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_UnixEpoch_ReturnsCorrectDateTime()
    {
        // 2025-01-01T00:00:00Z
        var result = ExpiryParser.Parse("1735689600");

        result.Should().NotBeNull();
        result!.Value.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_Iso8601_ReturnsCorrectDateTime()
    {
        var result = ExpiryParser.Parse("2025-06-15T12:00:00Z");

        result.Should().NotBeNull();
        result!.Value.Should().Be(new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_NullInput_ReturnsOneWeekFromNow()
    {
        var before = DateTime.UtcNow.AddDays(7);
        var result = ExpiryParser.Parse(null);
        var after = DateTime.UtcNow.AddDays(7);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public void Parse_NullInputWithDefault_ReturnsDefault()
    {
        var defaultExpiry = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = ExpiryParser.Parse(null, defaultExpiry);

        result.Should().Be(defaultExpiry);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("5x")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0d")]
    [InlineData("0h")]
    public void Parse_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => ExpiryParser.Parse(input);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Invalid expiry format: {input}");
    }
}
