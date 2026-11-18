using System.Globalization;

namespace Benchline.Contracts;

public static class AstanaTime
{
    public const string Zone = "Astana time (UTC+5)";
    public const string ShortZone = "UTC+5";
    private static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    public static DateTimeOffset Convert(DateTimeOffset instant) => instant.ToOffset(Offset);

    public static string Format(DateTimeOffset instant, string format) =>
        Convert(instant).ToString(format, CultureInfo.InvariantCulture);
}
