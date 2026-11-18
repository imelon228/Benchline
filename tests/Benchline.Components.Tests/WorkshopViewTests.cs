using Benchline.Client.Pages;
using Benchline.Components;
using Benchline.Contracts;
using Bunit;
using Xunit;

namespace Benchline.Components.Tests;

public sealed class WorkshopViewTests : BunitContext
{
    private static readonly StationDto Station = new(Guid.NewGuid(), "Laser bench", "Laser cutting", "Bay 1", true, 0);
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly SlotDto Slot = new(Guid.NewGuid(), Station.Id, Now.AddHours(1), Now.AddHours(2), true);
    private static readonly BoardDto Board = new(2, Now, [Station], [Slot], []);

    [Fact]
    public void ScheduleRoutesTheSelectedSlotAndRowActionByTheirTypedIds()
    {
        Guid selectedStation = default;
        SlotDto? selectedSlot = null;
        var grid = Render<ScheduleGrid>(parameters => parameters
            .Add(item => item.Board, Board).Add(item => item.IsMember, true)
            .Add(item => item.CanWrite, true)
            .Add(item => item.ReserveStation, value => selectedStation = value)
            .Add(item => item.ReserveSlot, value => selectedSlot = value));

        grid.Find("button[data-slot]").Click();
        Assert.Equal(Slot, selectedSlot);
        grid.Find(".station-actions button").Click();
        Assert.Equal(Station.Id, selectedStation);
        grid.Render(parameters => parameters.Add(item => item.PendingSlot, Slot.Id));
        Assert.True(grid.Find(".station-actions button").HasAttribute("disabled"));
        Assert.Contains("Saving", grid.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduleMarksOwnedAndUnavailableSessionsAndFiltersStations()
    {
        var owned = new BookingDto(Guid.NewGuid(), Slot.Id, "Reserved", Slot.End);
        var offline = Station with { Id = Guid.NewGuid(), Name = "Backup", Online = false };
        var offlineSlot = Slot with { Id = Guid.NewGuid(), StationId = offline.Id, Available = false };
        var board = Board with { Stations = [Station, offline], Slots = [Slot, offlineSlot], Bookings = [owned] };
        var grid = Render<ScheduleGrid>(parameters => parameters.Add(item => item.Board, board));
        Assert.Contains("Yours", grid.Find(".slot-own").TextContent, StringComparison.Ordinal);
        Assert.Contains("Offline", grid.Find(".slot-out").TextContent, StringComparison.Ordinal);
        grid.Render(parameters => parameters.Add(item => item.Filter, "CNC milling"));
        Assert.NotNull(grid.Find(".empty"));
    }

    [Fact]
    public void ScheduleOmitsEndedColumnsAndDescribesDisabledReservations()
    {
        var offline = Station with { Id = Guid.NewGuid(), Online = false };
        var empty = Station with { Id = Guid.NewGuid(), Name = "Empty station" };
        var ended = Slot with { Start = Now.AddHours(-1), End = Now };
        var current = Slot with { Id = Guid.NewGuid(), Start = Now, End = Now.AddHours(1), Available = false };
        var future = Slot with { Id = Guid.NewGuid(), StationId = offline.Id, Available = false };
        var board = Board with { Stations = [Station, offline, empty], Slots = [ended, current, future] };
        var grid = Render<ScheduleGrid>(parameters => parameters.Add(item => item.Board, board)
            .Add(item => item.IsMember, true).Add(item => item.CanWrite, true));

        Assert.Equal(2, grid.FindAll(".time-heading").Count);
        Assert.Contains("In progress", grid.Markup, StringComparison.Ordinal);
        Assert.All(grid.FindAll(".slot-gap"), cell => Assert.Empty(cell.TextContent.Trim()));
        Assert.Contains("No open sessions right now", grid.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.All(grid.FindAll(".station-actions button"), button =>
        {
            Assert.True(button.HasAttribute("disabled"));
            var reason = grid.Find("#" + button.GetAttribute("aria-describedby")).TextContent;
            Assert.Contains(reason, new[] { "No open sessions", "Station is out of service" });
        });
    }

    [Fact]
    public void ScheduleUsesTheAstanaDateForDayHeadersAndAccessibleTimes()
    {
        var utcEvening = new DateTimeOffset(2026, 12, 31, 18, 30, 0, TimeSpan.Zero);
        var tomorrow = Slot with { Start = utcEvening.AddMinutes(30), End = utcEvening.AddMinutes(90) };
        var board = Board with { AsOf = utcEvening, Slots = [tomorrow] };
        var grid = Render<ScheduleGrid>(parameters => parameters.Add(item => item.Board, board)
            .Add(item => item.IsMember, true).Add(item => item.CanWrite, true));
        Assert.Contains("Astana time (UTC+5)", grid.Find("caption").TextContent, StringComparison.Ordinal);
        Assert.Equal("00:00", grid.Find("time").TextContent);
        Assert.Equal("1 Jan", grid.Find(".time-heading span").TextContent);
        Assert.Contains("1 Jan, 00:00 UTC+5", grid.Find("button[data-slot]").GetAttribute("aria-label"), StringComparison.Ordinal);

        grid.Render(parameters => parameters.Add(item => item.Board, board with { AsOf = tomorrow.Start }));
        Assert.Empty(grid.FindAll(".time-heading span"));
    }

    [Fact]
    public void FilterEmitsCapabilityAndAnEmptySelection()
    {
        string? selected = null;
        var filter = Render<StationFilter>(parameters => parameters
            .Add(item => item.Capabilities, new[] { Station.Capability })
            .Add(item => item.ValueChanged, value => selected = value));
        filter.Find("select").Change(Station.Capability);
        Assert.Equal(Station.Capability, selected);
        filter.Find("select").Change("");
        Assert.Equal("", selected);
    }

    [Theory]
    [InlineData(true, true, "Live")]
    [InlineData(true, false, "Reconnecting")]
    [InlineData(false, false, "Offline")]
    public void ConnectionStatusNamesTheCurrentState(bool online, bool live, string label)
    {
        var chip = Render<ConnectionStatus>(parameters => parameters
            .Add(item => item.Online, online).Add(item => item.Live, live));
        Assert.Equal(label, chip.Find(".status-chip").TextContent);
    }

    [Fact]
    public void WorkshopNoticesReportsOfflineAndDismissesErrors()
    {
        var dismissed = false;
        var notices = Render<WorkshopNotices>(parameters => parameters.Add(item => item.Online, false)
            .Add(item => item.Error, "Reservation expired.").Add(item => item.Dismissed, () => dismissed = true));
        Assert.Contains("Reservation expired.", notices.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Contains("You are offline", notices.Find("[role=status]").TextContent, StringComparison.Ordinal);
        notices.Find("[role=alert] button").Click();
        Assert.True(dismissed);
    }

    [Fact]
    public void BookingDialogReportsValidationAndForwardsSelectionAndClose()
    {
        var closed = false;
        var hours = 0;
        var dialog = Render<BookingDialog>(parameters => parameters
            .Add(item => item.StationName, Station.Name).Add(item => item.Slots, new[] { Slot })
            .Add(item => item.SelectedSlot, Slot.Id).Add(item => item.Error, "Session unavailable.")
            .Add(item => item.Close, () => closed = true).Add(item => item.ExtraHoursChanged, value => hours = value));
        Assert.Equal("reserve-title", dialog.Find("dialog").GetAttribute("aria-labelledby"));
        Assert.Contains("Astana time (UTC+5)", dialog.Find("label[for=session-slot]").TextContent, StringComparison.Ordinal);
        Assert.Contains("06:00 UTC+5", dialog.Find("#session-slot option").TextContent, StringComparison.Ordinal);
        Assert.Contains("07:00", dialog.Find("#deadline-extra option").TextContent, StringComparison.Ordinal);
        Assert.Contains("Session unavailable.", dialog.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.True(dialog.Find("button[type=submit]").HasAttribute("disabled"));
        dialog.Find("#deadline-extra").Change("3");
        Assert.Equal(3, hours);
        dialog.Find("button[type=button]").Click();
        Assert.True(closed);
    }

    [Fact]
    public void BookingsListShowsEmptyStateAndForwardsCancellation()
    {
        Guid cancelled = default;
        var list = Render<BookingsList>(parameters => parameters
            .Add(item => item.Board, Board).Add(item => item.CanWrite, true)
            .Add(item => item.Cancel, value => cancelled = value));
        Assert.NotNull(list.Find(".empty"));
        var booking = new BookingDto(Guid.NewGuid(), Slot.Id, "Reserved", Slot.End);
        list.Render(parameters => parameters.Add(item => item.Board, Board with { Bookings = [booking] }));
        list.Find("button").Click();
        Assert.Equal(booking.Id, cancelled);
    }

    [Fact]
    public void RecoveryQueueListsOnlyDisplacedStations()
    {
        var queue = Render<RecoveryQueue>(parameters => parameters.Add(item => item.Stations, new[] { Station }));
        Assert.NotNull(queue.Find(".empty"));
        queue.Render(parameters => parameters.Add(item => item.Stations, new[] { Station with { Displaced = 2 } }));
        Assert.Contains("2 waiting for replacement", queue.Find("li").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryDialogDisablesPublishingAStalePreview()
    {
        var dialog = Render<RecoveryDialog>(parameters => parameters.Add(item => item.Board, Board)
            .Add(item => item.Plan, new PlanDto(1, [], 1)).Add(item => item.CanWrite, true));
        Assert.True(dialog.Find(".primary-button").HasAttribute("disabled"));
        Assert.Contains("The schedule changed.", dialog.Markup, StringComparison.Ordinal);
        Assert.NotNull(dialog.Find(".empty"));

        dialog.Render(parameters => parameters.Add(item => item.Plan,
            new PlanDto(1, [new MoveDto(Guid.NewGuid(), Guid.NewGuid())], 0)));
        Assert.Contains("Replacement session is no longer on the board.", dialog.Markup, StringComparison.Ordinal);
        var publish = dialog.Find(".primary-button");
        Assert.True(publish.HasAttribute("disabled"));
        Assert.Equal("publish-reason", publish.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void AuditHistoryUsesTheDisplayedCursorForPaging()
    {
        long selected = 0;
        var audit = Render<AuditLog>(parameters => parameters.Add(item => item.Online, true)
            .Add(item => item.NextCursor, 8)
            .Add(item => item.LoadOlder, value => selected = value));
        Assert.NotNull(audit.Find(".empty"));
        Assert.Equal("api/audit.csv", audit.Find("a").GetAttribute("href"));
        audit.Find("button").Click();
        Assert.Equal(8, selected);
    }

    [Fact]
    public void HeaderShowsOnlyCoordinatorNavigationAndEmitsSelectedView()
    {
        BoardView selected = BoardView.Board;
        var header = Render<WorkshopHeader>(parameters => parameters
            .Add(item => item.Theme, new ThemeSetting(false, () => Task.CompletedTask))
            .Add(item => item.Session, new SessionDto("Coordinator", "Coordinator"))
            .Add(item => item.IsCoordinator, true).Add(item => item.ViewChanged, view => selected = view));
        Assert.DoesNotContain("My bookings", header.Markup, StringComparison.Ordinal);
        Assert.Equal("page", header.Find("nav button").GetAttribute("aria-current"));
        header.FindAll("nav button")[1].Click();
        Assert.Equal(BoardView.Audit, selected);
    }
}
