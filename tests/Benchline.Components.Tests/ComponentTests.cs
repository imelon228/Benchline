using Benchline.Client;
using Benchline.Components;
using Benchline.Contracts;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Benchline.Components.Tests;

public sealed class ComponentTests : BunitContext
{
    [Fact]
    public void NoticeInvokesTheDismissCallback()
    {
        var dismissed = false;
        var notice = Render<NoticeBar>(parameters => parameters
            .Add(component => component.Text, "Session closed.")
            .Add(component => component.Error, true)
            .Add(component => component.OnDismiss, () => dismissed = true));
        Assert.Equal("Session closed.", notice.Find("[role=alert] span").TextContent);
        notice.Find("button[aria-label='Dismiss message']").Click();
        Assert.True(dismissed);
    }

    [Fact]
    public void ListUsesItsTemplateAndRendersAnEmptyMessage()
    {
        RenderFragment<string> template = item => builder => builder.AddContent(0, item.ToUpperInvariant());
        var list = Render<ItemStack<string>>(parameters => parameters
            .Add(component => component.Items, new[] { "first", "second" })
            .Add(component => component.Label, "Stations")
            .Add(component => component.ItemTemplate, template));
        Assert.Equal(new[] { "FIRST", "SECOND" }, list.FindAll("li").Select(entry => entry.TextContent));
        list.Render(parameters => parameters.Add(component => component.Items, Array.Empty<string>()));
        Assert.NotNull(list.Find(".empty"));
    }

    [Fact]
    public void ReducerDropsOldSnapshotsAndReleasesOptimisticReservationsOnFailure()
    {
        var slotId = Guid.NewGuid();
        var stationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var board = new BoardDto(4, now, [], [new SlotDto(slotId, stationId, now, now.AddHours(1), true)], []);
        var signedIn = BoardStore.Reduce(new(), new BoardAction.SessionChanged(new SessionDto("Member", "Member")));
        var current = BoardStore.Reduce(signedIn, new BoardAction.BoardLoaded(board));
        var optimistic = BoardStore.Reduce(current, new BoardAction.WriteStarted(slotId));
        Assert.Empty(optimistic.AvailableFor(stationId));
        Assert.False(optimistic.CanWrite);
        Assert.Same(optimistic, BoardStore.Reduce(optimistic, new BoardAction.BoardLoaded(board with { Revision = 3 })));
        var failed = BoardStore.Reduce(optimistic, new BoardAction.Failed("Slot changed."));
        Assert.Single(failed.AvailableFor(stationId));
        Assert.True(failed.CanWrite);
    }
}
