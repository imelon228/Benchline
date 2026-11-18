using Benchline.Contracts;

namespace Benchline.Client;

public sealed record BoardState(SessionDto? Session = null, BoardDto? Board = null, bool Loading = true,
    bool Live = false, bool Online = true, bool Busy = false, Guid? PendingSlot = null, string? Error = null,
    string? Notice = null)
{
    public bool IsMember => Session?.Role == "Member";
    public bool IsCoordinator => Session?.Role == "Coordinator";
    public bool CanWrite => Online && !Busy && Board is not null;

    public IEnumerable<SlotDto> AvailableFor(Guid station) =>
        Board?.Slots.Where(slot => slot.StationId == station && slot.Available && slot.Id != PendingSlot) ?? [];

    public int AvailableCount => Board?.Slots.Count(slot => slot.Available && slot.Id != PendingSlot) ?? 0;
}

public abstract record BoardAction
{
    public sealed record SessionChanged(SessionDto? Session) : BoardAction;
    public sealed record BoardLoaded(BoardDto Board) : BoardAction;
    public sealed record LoadStarted : BoardAction;
    public sealed record ConnectionChanged(bool Online, bool Live) : BoardAction;
    public sealed record WriteStarted(Guid? Slot = null) : BoardAction;
    public sealed record WriteFinished(string? Notice = null) : BoardAction;
    public sealed record Failed(string Message) : BoardAction;
    public sealed record Dismissed : BoardAction;
}

public sealed class BoardStore
{
    public BoardState State { get; private set; } = new();
    public event Action? Changed;

    public void Dispatch(BoardAction action)
    {
        var next = Reduce(State, action);
        if (next == State)
            return;
        State = next;
        Changed?.Invoke();
    }

    public static BoardState Reduce(BoardState state, BoardAction action) => action switch
    {
        BoardAction.SessionChanged(var session) => new BoardState(Session: session, Loading: session is not null, Online: state.Online),
        BoardAction.LoadStarted => state with { Loading = true, Error = null },
        BoardAction.BoardLoaded(var board) when state.Session is not null && board.Revision >= (state.Board?.Revision ?? 0) =>
            state with { Board = board, Loading = false },
        BoardAction.ConnectionChanged(var online, var live) => state with { Online = online, Live = online && live },
        BoardAction.WriteStarted(var slot) => state with { Busy = true, PendingSlot = slot, Error = null, Notice = null },
        BoardAction.WriteFinished(var notice) => state with { Busy = false, PendingSlot = null, Notice = notice },
        BoardAction.Failed(var message) => state with { Loading = false, Busy = false, PendingSlot = null, Error = message },
        BoardAction.Dismissed => state with { Error = null, Notice = null },
        _ => state
    };
}
