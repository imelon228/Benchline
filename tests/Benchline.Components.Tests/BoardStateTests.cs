using System.Net;
using System.Net.Http.Json;
using Benchline.Client;
using Benchline.Contracts;
using Xunit;

namespace Benchline.Components.Tests;

public sealed class BoardStateTests
{
    [Fact]
    public void SessionChangeClearsPrivateDataAndDisconnectingDisablesWrites()
    {
        var store = new BoardStore();
        store.Dispatch(new BoardAction.SessionChanged(new SessionDto("Member", "Member")));
        store.Dispatch(new BoardAction.BoardLoaded(new BoardDto(1, DateTimeOffset.UnixEpoch, [], [], [])));
        Assert.True(store.State.CanWrite);
        store.Dispatch(new BoardAction.ConnectionChanged(false, true));
        Assert.False(store.State.CanWrite);
        Assert.False(store.State.Live);
        store.Dispatch(new BoardAction.SessionChanged(new SessionDto("Coordinator", "Coordinator")));
        Assert.Null(store.State.Board);
        Assert.True(store.State.IsCoordinator);
        Assert.False(store.State.IsMember);
    }

    [Fact]
    public void CompletionKeepsSuccessNoticeUntilDismissedAndIgnoresUnchangedState()
    {
        var store = new BoardStore();
        var changes = 0;
        store.Changed += () => changes++;
        store.Dispatch(new BoardAction.WriteStarted(Guid.NewGuid()));
        store.Dispatch(new BoardAction.WriteFinished("Saved."));
        Assert.False(store.State.Busy);
        Assert.Null(store.State.PendingSlot);
        Assert.Equal("Saved.", store.State.Notice);
        store.Dispatch(new BoardAction.Dismissed());
        Assert.Null(store.State.Notice);
        store.Dispatch(new BoardAction.Dismissed());
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task ApiReturnsExpectedConflictWithItsProblemDetails()
    {
        using var client = new HttpClient(new Responses()) { BaseAddress = new Uri("https://benchline.test/") };
        var reply = await new WorkshopApi(client).Send<RevisionDto>(HttpMethod.Post, "api/bookings");
        Assert.Equal(new ApiFailure(HttpStatusCode.Conflict, "The schedule changed."), reply.Error);
    }

    private sealed class Responses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/token")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new TokenDto("test-token")) });

            Assert.Equal("test-token", Assert.Single(request.Headers.GetValues("X-Workshop-Token")));
            var content = JsonContent.Create(new { detail = "The schedule changed." });
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = content });
        }
    }
}
