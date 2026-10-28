using System.Globalization;
using System.Text.RegularExpressions;
namespace Hampcoders.Electrolink.API.Shared.Application.Time;

// HTTP interval convention shared by sample and alert histories: UTC, microseconds, at most 31 days.
public sealed record ExplicitUtcInterval(DateTime FromUtc, DateTime ToUtc)
{
    public static ExplicitUtcInterval Parse(string? from, string? to)
    {
        var start = Timestamp(from); var end = Timestamp(to);
        if (start >= end || end - start > TimeSpan.FromDays(31))
            throw new ArgumentException("from must precede to; interval must not exceed 31 days.");
        return new(start, end);
    }
    private static DateTime Timestamp(string? value)
    {
        if (value == null || value.Length > 40 || !Regex.IsMatch(value,
            @"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,6})?(Z|[+-]\d{2}:\d{2})\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
            throw new ArgumentException("from and to require an ISO timestamp with explicit offset and at most 6 fractional digits.");
        var text = value.EndsWith('Z') ? value[..^1] + "+00:00" : value;
        if (!DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Invalid from or to timestamp.");
        return parsed.UtcDateTime;
    }
}
