using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ThreeOfSpades.Api.Auth;
using ThreeOfSpades.Api.Contracts;
using ThreeOfSpades.Api.Services;

namespace ThreeOfSpades.Api.Hubs;

[Authorize]
public class GameHub(LiveGameService games, RoomService rooms) : Hub
{
    private Guid UserId => JwtTokenService.UserId(Context.User!);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{UserId}");
        await base.OnConnectedAsync();
    }

    public async Task JoinRoom(Guid roomId)
    {
        await rooms.Get(UserId, roomId, Context.ConnectionAborted);
        Context.Items["roomId"] = roomId;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room:{roomId}");
        await games.Heartbeat(UserId, roomId);
        var live = games.Get(roomId);
        if (live is not null)
            await Clients.Caller.SendAsync("gameUpdated", LiveGameService.Snapshot(live.State, UserId));
    }

    public Task PlaceBid(Guid roomId, int amount) => games.Bid(UserId, roomId, amount);

    public Task PassBid(Guid roomId) => games.Pass(UserId, roomId);

    public Task Select(Guid roomId, string trump, List<PartnerConditionDto> conditions) =>
        games.Select(UserId, roomId, trump, conditions.Select(c => c.ToModel()).ToList());

    public Task PlayCard(Guid roomId, string cardId) => games.Play(UserId, roomId, cardId);

    public Task Heartbeat(Guid roomId) => games.Heartbeat(UserId, roomId);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("roomId", out var value) && value is Guid roomId)
            await games.MarkOffline(UserId, roomId);
        await base.OnDisconnectedAsync(exception);
    }
}
