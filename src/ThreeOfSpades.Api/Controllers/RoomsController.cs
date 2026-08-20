using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThreeOfSpades.Api.Auth;
using ThreeOfSpades.Api.Contracts;
using ThreeOfSpades.Api.Services;

namespace ThreeOfSpades.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rooms")]
public class RoomsController(RoomService rooms, LiveGameService games) : ControllerBase
{
    private Guid Me => JwtTokenService.UserId(User);

    [HttpGet]
    public async Task<ActionResult<List<RoomDto>>> List(CancellationToken ct) =>
        Ok(await rooms.ListForUser(Me, ct));

    [HttpPost]
    public async Task<ActionResult<RoomDto>> Create(CreateRoomRequest req, CancellationToken ct) =>
        Ok(await rooms.Create(Me, req.Name, ct));

    [HttpPost("join")]
    public async Task<ActionResult<RoomDto>> Join(JoinRoomRequest req, CancellationToken ct) =>
        Ok(await rooms.Join(Me, req.Code, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RoomDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await rooms.Get(Me, id, ct));

    [HttpPost("{id:guid}/ready")]
    public async Task<ActionResult<RoomDto>> Ready(Guid id, CancellationToken ct) =>
        Ok(await rooms.ToggleReady(Me, id, ct));

    [HttpPost("{id:guid}/bots")]
    public async Task<ActionResult<RoomDto>> Bots(Guid id, CancellationToken ct) =>
        Ok(await rooms.FillBots(Me, id, ct));

    [HttpPost("{id:guid}/kick")]
    public async Task<ActionResult<RoomDto>> Kick(Guid id, KickRequest req, CancellationToken ct) =>
        Ok(await rooms.Kick(Me, id, req.UserId, ct));

    [HttpPost("{id:guid}/transfer")]
    public async Task<ActionResult<RoomDto>> Transfer(Guid id, TransferRequest req, CancellationToken ct) =>
        Ok(await rooms.Transfer(Me, id, req.UserId, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await rooms.Archive(Me, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        await rooms.Leave(Me, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<GameSnapshotDto>> Start(Guid id, CancellationToken ct) =>
        Ok(await games.Start(Me, id, ct));

    [HttpGet("{id:guid}/game")]
    public ActionResult<GameSnapshotDto> Game(Guid id) => Ok(games.SnapshotFor(Me, id));

    [HttpPost("{id:guid}/game/bid")]
    public async Task<ActionResult<GameSnapshotDto>> Bid(Guid id, BidRequest req) =>
        Ok(await games.Bid(Me, id, req.Amount));

    [HttpPost("{id:guid}/game/pass")]
    public async Task<ActionResult<GameSnapshotDto>> Pass(Guid id) =>
        Ok(await games.Pass(Me, id));

    [HttpPost("{id:guid}/game/select")]
    public async Task<ActionResult<GameSnapshotDto>> Select(Guid id, SelectRequest req) =>
        Ok(await games.Select(Me, id, req.Trump, req.Conditions.Select(c => c.ToModel()).ToList()));

    [HttpPost("{id:guid}/game/play")]
    public async Task<ActionResult<GameSnapshotDto>> Play(Guid id, PlayRequest req) =>
        Ok(await games.Play(Me, id, req.CardId));
}
