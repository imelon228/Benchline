using Benchline.Contracts;

namespace Benchline.Client.Pages;

internal static class WorkshopFormat
{
    internal static string Date(DateTimeOffset date) => AstanaTime.Format(date, "ddd d MMM yyyy");

    internal static string Time(DateTimeOffset date) => $"{AstanaTime.Format(date, "ddd d MMM, HH:mm")} {AstanaTime.ShortZone}";

    internal static string ShortId(Guid id) => id.ToString("N")[..8];
}
