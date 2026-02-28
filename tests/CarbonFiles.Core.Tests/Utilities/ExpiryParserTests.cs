using CarbonFiles.Core.Utilities;
using FluentAssertions;
using Xunit;

namespace CarbonFiles.Core.Tests.Utilities;

public class ExpiryParserTests
{
    [Theory]
    [InlineData("15m", 15)]
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
    [InlineData("6h", 6)]
    [InlineData("12h", 12)]
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
    public void Parse_DurationWeeks_ReturnsCorrectTime(string input, int expectedDays)
    {
        var before = DateTime.UtcNow.AddDays(expectedDays);
        var result = ExpiryParser.Parse(input);
        var after = DateTime.UtcNow.AddDays(expectedDays);

        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(before.AddSeconds(-1));
        result.Value.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    [Fact]
    public void Parse_DurationMonth_ReturnsCorrectTime()
    {
        var before = DateTime.UtcNow.AddDays(30);
        var result = ExpiryParser.Parse("1m");
        var after = DateTime.UtcNow.AddDays(30);

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
    public void Parse_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => ExpiryParser.Parse(input);

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Invalid expiry format: {input}");
    }
}
