namespace Benchline.Contracts;

public static class DemoCatalog
{
    public static readonly DemoStation Primary = new("Laser Cutter 01", "Laser cutting", "Coworking Zone");
    public static readonly DemoStation Backup = new("Laser Cutter 02", "Laser cutting", "Open Space");
    public static readonly DemoStation Printer = new("Prusa MK4", "3D printing", "Library");
    public static readonly DemoStation Mill = new("Desktop CNC", "CNC milling", "FabLab");

    public static IReadOnlyList<DemoStation> Stations { get; } = Array.AsReadOnly(new[] { Primary, Backup, Printer, Mill });

    public static string? DisplayName(string accountId) => accountId switch
    {
        "member" => "Aidos Serikov",
        "member2" => "Aruzhan Nurgaliyeva",
        "tech" => "Nurlan Tolegenov",
        "coord" => "Dana Kassymova",
        _ => null
    };

    public sealed record DemoStation(string Name, string Capability, string Location);
}
