using System.Net;
using Benchline.Components;
using Benchline.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Benchline.Client.Pages;

public partial class Home
{
    private static readonly (string Role, string Username)[] DemoAccounts =
        [("Member", "member"), ("Member", "member2"), ("Technician", "tech"), ("Coordinator", "coord")];

    private string DocumentTitle => State.Session is null ? "Sign in | Benchline" : _view switch
    {
        BoardView.Bookings => "My bookings | Benchline",
        BoardView.Audit => "Audit log | Benchline",
        _ => "Workshop board | Benchline"
    };

    private BoardState State => Store.State;
    private string WriteReason => State.Online ? "Wait for the current update" : "Reconnect to change the schedule";
    private ThemeSetting Theme => new(_dark, ToggleTheme);
    private readonly SemaphoreSlim _refresh = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private DotNetObjectReference<Home>? _reference;
    private HubConnection? _hub;
    private BoardView _view;
    private bool _dark;
    private string _username = "";
    private string _password = "";
    private string _filter = "";
    private Guid? _bookingStation;
    private Guid _selectedSlot;
    private int _extraHours = 1;
    private Guid? _planStation;
    private PlanDto? _plan;
    private string? _dialogToOpen;
    private AuditDto[] _audit = [];
    private long? _nextAudit;
    private bool _auditLoading;

    protected override void OnInitialized() => Store.Changed += Changed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _reference = DotNetObjectReference.Create(this);
            _dark = await Browser.InvokeAsync<bool>("benchline.connect", _reference);
            await LoadSession();
            await Browser.InvokeVoidAsync("benchline.prepareOffline");
            _ = Pulse();
        }

        if (_dialogToOpen is { } dialog)
        {
            _dialogToOpen = null;
            await Browser.InvokeVoidAsync("benchline.openDialog", dialog);
        }
    }

    private async Task LoadSession()
    {
        Store.Dispatch(new BoardAction.LoadStarted());
        try
        {
            if (!Receive(await Api.Session(), out var session))
                return;
            Store.Dispatch(new BoardAction.SessionChanged(session));
            if (State.Session is not null)
            {
                await StartLive();
            }
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
    }

    private async Task SignIn()
    {
        if (State.Busy)
            return;
        Store.Dispatch(new BoardAction.WriteStarted());
        try
        {
            var reply = await Api.Send<SessionDto>(HttpMethod.Post, "api/session", new SignInRequest(_username, _password));
            if (!Receive(reply, out var session))
                return;
            _password = "";
            Store.Dispatch(new BoardAction.SessionChanged(session));
            await StartLive();
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
    }

    private async Task SignOut()
    {
        Store.Dispatch(new BoardAction.WriteStarted());
        try
        {
            if (!Receive(await Api.Send<object>(HttpMethod.Post, "api/sign-out"), out _))
                return;
            await ClearSession();
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
    }

    private async Task ClearSession()
    {
        Store.Dispatch(new BoardAction.SessionChanged(null));
        _view = BoardView.Board;
        _filter = "";
        _audit = [];
        _nextAudit = null;
        _bookingStation = null;
        _plan = null;
        _planStation = null;
        await StopLive();
    }

    private async Task Refresh()
    {
        if (State.Session is null || !State.Online)
            return;

        await _refresh.WaitAsync();
        try
        {
            var expectedSession = State.Session;
            if (expectedSession is null || _lifetime.IsCancellationRequested)
                return;

            var reply = await Api.Board();
            if (!ReferenceEquals(State.Session, expectedSession))
                return;
            if (reply.Error?.Status == HttpStatusCode.Unauthorized)
            {
                await ClearSession();
                Store.Dispatch(new BoardAction.Failed("Your workshop session expired. Sign in again."));
            }
            else if (Receive(reply, out var board))
            {
                Store.Dispatch(new BoardAction.BoardLoaded(board));
            }
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
        finally
        {
            _refresh.Release();
        }
    }

    private async Task StartLive()
    {
        if (State.Session is null || !State.Online || _hub is not null)
            return;

        var hub = new HubConnectionBuilder().WithUrl(Navigation.ToAbsoluteUri("live"))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20)])
            .Build();
        hub.On<long>("ScheduleChanged", revision =>
        {
            if (revision > (State.Board?.Revision ?? 0))
                _ = InvokeAsync(Refresh);
        });
        hub.Reconnecting += _ => InvokeAsync(() => Store.Dispatch(new BoardAction.ConnectionChanged(State.Online, false)));
        hub.Reconnected += _ => InvokeAsync(async () =>
        {
            Store.Dispatch(new BoardAction.ConnectionChanged(State.Online, true));
            await Refresh();
        });
        hub.Closed += _ => InvokeAsync(() => Store.Dispatch(new BoardAction.ConnectionChanged(State.Online, false)));
        _hub = hub;

        try
        {
            await hub.StartAsync(_lifetime.Token);
            Store.Dispatch(new BoardAction.ConnectionChanged(State.Online, true));
            await Refresh();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or
            OperationCanceledException or System.Net.WebSockets.WebSocketException)
        {
            await StopLive();
            await Refresh();
        }
    }

    private async Task StopLive()
    {
        var hub = _hub;
        _hub = null;
        if (hub is not null)
            await hub.DisposeAsync();
    }

    private async Task Pulse()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                await InvokeAsync(async () =>
                {
                    if (State.Session is null || !State.Online || State.Busy)
                        return;
                    if (_hub?.State == HubConnectionState.Disconnected)
                        await StopLive();
                    if (_hub is null)
                        await StartLive();
                    else
                        await Refresh();
                });
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    [JSInvokable]
    public async Task Connectivity(bool online)
    {
        Store.Dispatch(new BoardAction.ConnectionChanged(online, _hub?.State == HubConnectionState.Connected));
        if (!online)
            return;
        if (_hub?.State == HubConnectionState.Disconnected)
            await StopLive();
        if (State.Session is null)
            await LoadSession();
        else if (_hub is null)
            await StartLive();
        else
            await Refresh();
    }

    private void OpenBooking(Guid station) => OpenBooking(station, null);
    private void OpenSlot(SlotDto slot) => OpenBooking(slot.StationId, slot.Id);

    private void OpenBooking(Guid station, Guid? selectedSlot)
    {
        if (State.AvailableFor(station).FirstOrDefault(candidate => selectedSlot is null || candidate.Id == selectedSlot) is not { } slot)
            return;
        Dismiss();
        _bookingStation = station;
        _selectedSlot = slot.Id;
        _extraHours = 1;
        _dialogToOpen = "reserve-dialog";
    }

    private async Task CloseBooking()
    {
        await Browser.InvokeVoidAsync("benchline.closeDialog", "reserve-dialog");
        _bookingStation = null;
    }

    private async Task Reserve()
    {
        if (State.Board is not { } board || _bookingStation is not { } station)
            return;
        var slot = State.AvailableFor(station).FirstOrDefault(item => item.Id == _selectedSlot);
        if (slot is null)
        {
            Store.Dispatch(new BoardAction.Failed("Choose a session that is still open."));
            return;
        }

        if (await Write(HttpMethod.Post, "api/bookings",
            new ReserveRequest(board.Revision, Guid.NewGuid(), slot.Id, slot.End.AddHours(_extraHours)),
            "Reservation confirmed.", slot.Id))
            await CloseBooking();
    }

    private Task Cancel(Guid booking) => Write(HttpMethod.Post, $"api/bookings/{booking}/cancel",
        new RevisionRequest(State.Board!.Revision), "Reservation cancelled.");

    private Task SetAvailability(StationDto station) => Write(HttpMethod.Put, $"api/stations/{station.Id}",
        new AvailabilityRequest(State.Board!.Revision, !station.Online),
        station.Online ? "Outage reported. Affected members can see the change." : "Station restored.");

    private async Task Preview(Guid station)
    {
        if (!State.CanWrite)
            return;
        Store.Dispatch(new BoardAction.WriteStarted());
        try
        {
            if (!Receive(await Api.Preview(station), out var plan))
                return;
            _plan = plan;
            _planStation = station;
            _dialogToOpen = "plan-dialog";
            Store.Dispatch(new BoardAction.WriteFinished());
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
    }

    private async Task ClosePlan()
    {
        await Browser.InvokeVoidAsync("benchline.closeDialog", "plan-dialog");
        _plan = null;
        _planStation = null;
    }

    private async Task Publish(Guid station, PlanDto plan)
    {
        var board = State.Board!;
        var destinations = string.Join(", ", plan.Moves
            .Join(board.Slots, move => move.SlotId, slot => slot.Id, (_, slot) => slot.StationId)
            .Join(board.Stations, id => id, item => item.Id, (_, item) => item.Location)
            .Distinct().Order());
        var destination = destinations.Length == 0 ? "" : $" to {destinations}";
        if (await Write(HttpMethod.Post, $"api/recovery/{station}", new PublishRequest(plan.Revision, plan.Moves),
            $"Recovery published. {plan.Moves.Length} reservations moved{destination}."))
            await ClosePlan();
    }

    private async Task<bool> Write(HttpMethod method, string path, object input, string notice, Guid? optimisticSlot = null)
    {
        if (!State.CanWrite)
            return false;
        Store.Dispatch(new BoardAction.WriteStarted(optimisticSlot));
        try
        {
            var reply = await Api.Send<RevisionDto>(method, path, input);
            if (reply.Error?.Status == HttpStatusCode.Conflict)
                await Refresh();
            if (!Receive(reply, out _))
                return false;
            await Refresh();
            Store.Dispatch(new BoardAction.WriteFinished(notice));
            return true;
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
            return false;
        }
    }

    private void ChangeView(BoardView view)
    {
        Dismiss();
        _view = view;
    }

    private Task NavigateView(BoardView view)
    {
        if (view == BoardView.Audit)
            return OpenAudit();

        ChangeView(view);
        return Task.CompletedTask;
    }

    private async Task OpenAudit()
    {
        ChangeView(BoardView.Audit);
        await LoadAudit(null);
    }

    private async Task LoadAudit(long? before)
    {
        if (_auditLoading || !State.Online)
            return;
        _auditLoading = true;
        try
        {
            if (!Receive(await Api.Audit(before), out var rows))
                return;
            _audit = before is null ? rows : [.. _audit, .. rows];
            _nextAudit = rows.Length == AuditQuery.DefaultPageSize ? rows[^1].Revision : null;
        }
        catch (Exception exception) when (IsRequestFailure(exception))
        {
            Fail(exception);
        }
        finally
        {
            _auditLoading = false;
        }
    }

    private async Task ToggleTheme()
    {
        _dark = !_dark;
        await Browser.InvokeVoidAsync("benchline.theme", _dark);
    }

    private void Dismiss() => Store.Dispatch(new BoardAction.Dismissed());
    private void Changed() => _ = InvokeAsync(StateHasChanged);
    private static bool IsRequestFailure(Exception exception) => exception is HttpRequestException
        or TimeoutRejectedException or BrokenCircuitException or OperationCanceledException;
    private bool Receive<T>(ApiReply<T> reply, out T value)
    {
        value = reply.Value;
        if (reply.Error is not { } error)
            return true;

        Store.Dispatch(new BoardAction.Failed(error.Message));
        return false;
    }

    private void Fail(Exception _) => Store.Dispatch(
        new BoardAction.Failed("The workshop could not be reached. Try again when you are connected."));

    public async ValueTask DisposeAsync()
    {
        Store.Changed -= Changed;
        await _lifetime.CancelAsync();
        await StopLive();
        if (_reference is not null)
        {
            await Browser.InvokeVoidAsync("benchline.disconnect");
            _reference.Dispose();
        }
    }
}
