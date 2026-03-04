using System.Text.RegularExpressions;

namespace CarbonFiles.Core.Utilities;

public static partial class ExpiryParser
{
    [GeneratedRegex(@"^(\d+)([smhdw])$")]
    private static partial Regex DurationPattern();

    /// <summary>
    /// Returns null for "never", DateTime for everything else.
    /// Default (null input) returns 1 week from now.
    /// Supported formats: duration (e.g. 30s, 15m, 2h, 7d, 2w), Unix epoch, ISO 8601.
    /// </summary>
    public static DateTime? Parse(string? value, DateTime? defaultExpiry = null)
    {
        if (value == null)
            return defaultExpiry ?? DateTime.UtcNow.AddDays(7);

        if (value == "never")
            return null;

        // Duration: number + unit suffix (e.g. 30s, 15m, 2h, 7d, 2w)
        var match = DurationPattern().Match(value);
        if (match.Success)
        {
            var amount = long.Parse(match.Groups[1].Value);
            if (amount <= 0)
                throw new ArgumentException($"Invalid expiry format: {value}");

            return match.Groups[2].Value switch
            {
                "s" => DateTime.UtcNow.AddSeconds(amount),
                "m" => DateTime.UtcNow.AddMinutes(amount),
                "h" => DateTime.UtcNow.AddHours(amount),
                "d" => DateTime.UtcNow.AddDays(amount),
                "w" => DateTime.UtcNow.AddDays(amount * 7),
                _ => throw new ArgumentException($"Invalid expiry format: {value}")
            };
        }

        // Unix epoch: all digits
        if (long.TryParse(value, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;

        // ISO 8601: contains 'T'
        if (value.Contains('T') && DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var iso))
            return iso;

        throw new ArgumentException($"Invalid expiry format: {value}");
    }
}
