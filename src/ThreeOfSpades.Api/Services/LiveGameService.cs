using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ThreeOfSpades.Api.Contracts;
using ThreeOfSpades.Api.Data;
using ThreeOfSpades.Api.Domain;
using ThreeOfSpades.Api.Hubs;
using ThreeOfSpades.Engine;

namespace ThreeOfSpades.Api.Services;

public sealed class LiveTable
{
    public static readonly TimeSpan TurnLimit = TimeSpan.FromMinutes(1);

    public GameState State { get; set; } = null!;
    public object Gate { get; } = new();
    public Dictionary<Guid, DateTime> LastSeen { get; } = [];
    public Dictionary<Guid, DateTime> OfflineSince { get; } = [];
    public DateTime TurnDeadline { get; set; }
    public int DeadlineSeat { get; set; } = -1;
    public GamePhase DeadlinePhase { get; set; }
}

public class LiveGameService(IServiceScopeFactory scopes, IHubContext<GameHub> hub)
{
    private readonly ConcurrentDictionary<Guid, LiveTable> _tables = new();

    public LiveTable? Get(Guid roomId) => _tables.TryGetValue(roomId, out var t) ? t : null;

    public IEnumerable<LiveTable> All() => _tables.Values;

    public async Task<GameSnapshotDto> Start(Guid ownerId, Guid roomId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await rooms.RequireStartable(ownerId, roomId, ct);
        var ordered = room.Members.OrderBy(m => m.JoinedAt).ToList();
        var ownerIndex = Math.Max(0, ordered.FindIndex(m => m.UserId == room.OwnerId));
        var dealer = (ownerIndex + room.DealerIndex) % ordered.Count;
        var seated = ordered.Select(m => (m.UserId, m.User.UserName, RoomService.IsDummy(m.User))).ToList();
        var state = GameEngine.DealNewGame(room.Id, seated, dealer);
        room.ActiveGameId = state.GameId;
        await db.SaveChangesAsync(ct);

        _tables.TryRemove(room.Id, out _);
        var table = new LiveTable { State = state };
        foreach (var p in state.Players)
            table.LastSeen[p.UserId] = DateTime.UtcNow;
        lock (table.Gate)
        {
            GameEngine.RunBots(state);
            RefreshTurnDeadline(table);
        }
        _tables[room.Id] = table;
        await Broadcast(table);
        return Snapshot(table, ownerId);
    }

    public Task<GameSnapshotDto> Bid(Guid userId, Guid roomId, int amount) =>
        Mutate(userId, roomId, (state, seat) => GameEngine.Bid(state, seat, amount));

    public Task<GameSnapshotDto> Pass(Guid userId, Guid roomId) =>
        Mutate(userId, roomId, (state, seat) => GameEngine.Pass(state, seat));

    public Task<GameSnapshotDto> Select(Guid userId, Guid roomId, string trump, IReadOnlyList<PartnerCondition> conditions) =>
        Mutate(userId, roomId, (state, seat) => GameEngine.SelectTrumpAndPartners(state, seat, trump, conditions));

    public Task<GameSnapshotDto> Play(Guid userId, Guid roomId, string cardId) =>
        Mutate(userId, roomId, (state, seat) => GameEngine.PlayCard(state, seat, cardId));

    public GameSnapshotDto SnapshotFor(Guid userId, Guid roomId)
    {
        var table = Require(roomId);
        lock (table.Gate)
        {
            if (table.State.Players.All(p => p.UserId != userId))
                throw new InvalidOperationException("You are not seated in this game.");
            return Snapshot(table, userId);
        }
    }

    public async Task Heartbeat(Guid userId, Guid roomId)
    {
        var table = Get(roomId);
        if (table is null) return;
        lock (table.Gate)
        {
            table.LastSeen[userId] = DateTime.UtcNow;
            table.OfflineSince.Remove(userId);
        }
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoomService>().SetOnline(userId, roomId, true, CancellationToken.None);
    }

    public async Task MarkOffline(Guid userId, Guid roomId)
    {
        var table = Get(roomId);
        if (table is not null)
        {
            lock (table.Gate)
                table.OfflineSince.TryAdd(userId, DateTime.UtcNow);
        }
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RoomService>().SetOnline(userId, roomId, false, CancellationToken.None);
    }

    public async Task TickDisconnects()
    {
        foreach (var table in _tables.Values)
        {
            EngineResult? result = null;
            var botsActed = false;
            lock (table.Gate)
            {
                var g = table.State;
                if (g.Phase == GamePhase.Complete)
                    result = new EngineResult { Ok = true, State = g, GameFinished = true };
                else if (g.Phase == GamePhase.Cancelled)
                    result = new EngineResult { Ok = true, State = g, Cancelled = true };
                else
                {
                    foreach (var p in g.Players.Where(x => !x.IsBot))
                    {
                        if (!table.OfflineSince.TryGetValue(p.UserId, out var since)) continue;
                        var gone = DateTime.UtcNow - since;
                        if (gone > TimeSpan.FromMinutes(5))
                        {
                            result = GameEngine.Cancel(g, "A player was offline for more than 5 minutes.");
                            break;
                        }
                    }
                    result ??= ExpireTurn(table);
                    if (result is null)
                    {
                        var beforeTurn = g.CurrentTurn;
                        var beforePhase = g.Phase;
                        var beforeLog = g.BidLog.Count;
                        GameEngine.RunBots(g);
                        if (g.Phase == GamePhase.Complete)
                            result = new EngineResult { Ok = true, State = g, GameFinished = true };
                        else if (g.Phase == GamePhase.Cancelled)
                            result = new EngineResult { Ok = true, State = g, Cancelled = true };
                        else
                            botsActed = g.CurrentTurn != beforeTurn || g.Phase != beforePhase || g.BidLog.Count != beforeLog;
                    }
                    else if (result.Ok && g.Phase is not GamePhase.Complete and not GamePhase.Cancelled)
                        GameEngine.RunBots(g);
                    RefreshTurnDeadline(table);
                }
            }
            if (result is not null)
                await After(table, result);
            else if (botsActed)
                await Broadcast(table);
        }
    }

    private async Task<GameSnapshotDto> Mutate(Guid userId, Guid roomId, Func<GameState, int, EngineResult> apply)
    {
        var table = Require(roomId);
        EngineResult result;
        lock (table.Gate)
        {
            var seat = table.State.Players.FindIndex(p => p.UserId == userId);
            if (seat < 0) throw new InvalidOperationException("You are not seated in this game.");
            result = apply(table.State, seat);
            if (!result.Ok) throw new InvalidOperationException(result.Error);
            RunBots(table.State);
            RefreshTurnDeadline(table);
        }
        await After(table, result);
        return Snapshot(table, userId);
    }

    public async Task TickBots()
    {
        foreach (var table in _tables.Values)
        {
            var finished = false;
            var cancelled = false;
            var acted = false;
            lock (table.Gate)
            {
                var g = table.State;
                if (g.Phase == GamePhase.Complete)
                    finished = true;
                else if (g.Phase == GamePhase.Cancelled)
                    cancelled = true;
                else
                {
                    var beforeTurn = g.CurrentTurn;
                    var beforePhase = g.Phase;
                    var beforeLog = g.BidLog.Count;
                    var beforeTrick = g.CurrentTrick.Count;
                    GameEngine.RunBots(g);
                    if (g.Phase == GamePhase.Complete) finished = true;
                    else if (g.Phase == GamePhase.Cancelled) cancelled = true;
                    else
                        acted = g.CurrentTurn != beforeTurn || g.Phase != beforePhase
                            || g.BidLog.Count != beforeLog || g.CurrentTrick.Count != beforeTrick;
                    RefreshTurnDeadline(table);
                }
            }
            if (finished)
                await After(table, new EngineResult { Ok = true, State = table.State, GameFinished = true });
            else if (cancelled)
                await After(table, new EngineResult { Ok = true, State = table.State, Cancelled = true });
            else if (acted)
                await Broadcast(table);
        }
    }

    private static void RunBots(GameState g) => GameEngine.RunBots(g);

    private async Task After(LiveTable table, EngineResult result)
    {
        var finished = result.GameFinished || table.State.Phase == GamePhase.Complete;
        var cancelled = result.Cancelled || table.State.Phase == GamePhase.Cancelled;
        if (finished)
            await PersistFinished(table.State);
        else if (cancelled)
            await ClearActive(table.State.RoomId);
        await Broadcast(table, result.Notices);
    }

    private async Task PersistFinished(GameState g)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await db.Rooms.Include(r => r.Members).FirstAsync(r => r.Id == g.RoomId);
        if (await db.Games.AnyAsync(x => x.Id == g.GameId))
        {
            room.ActiveGameId = null;
            await db.SaveChangesAsync();
            _tables.TryRemove(g.RoomId, out _);
            return;
        }
        var record = new GameRecord
        {
            Id = g.GameId,
            RoomId = g.RoomId,
            PlayerCount = g.PlayerCount,
            Bid = g.Bid,
            Trump = g.Trump ?? "S",
            BidderName = g.Seat(g.BidderSeat ?? 0).UserName,
            Success = g.Success == true,
            TeamPoints = g.TeamPoints,
            PartnerConditionsJson = JsonSerializer.Serialize(g.Conditions),
            PlayedAt = DateTime.UtcNow,
            Players = g.Players.Select(p => new GamePlayerRecord
            {
                GameId = g.GameId,
                UserId = p.UserId,
                UserName = p.UserName,
                Seat = p.Seat,
                IsBidder = p.Seat == g.BidderSeat,
                IsPartner = g.PartnerSeats.Contains(p.Seat) && p.Seat != g.BidderSeat,
                PointsWon = p.PointsWon,
                ScoreDelta = p.ScoreDelta
            }).ToList()
        };
        db.Games.Add(record);
        room.ActiveGameId = null;
        room.DealerIndex = (room.DealerIndex + 1) % Math.Max(1, room.Members.Count);
        var users = await db.Users.Where(u => room.Members.Select(m => m.UserId).Contains(u.Id)).ToListAsync();
        foreach (var m in room.Members)
            m.Ready = users.FirstOrDefault(u => u.Id == m.UserId) is { } u && RoomService.IsDummy(u);
        await db.SaveChangesAsync();
        _tables.TryRemove(g.RoomId, out _);
    }

    private async Task ClearActive(Guid roomId)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await db.Rooms.FindAsync(roomId);
        if (room is not null)
        {
            room.ActiveGameId = null;
            await db.SaveChangesAsync();
        }
        _tables.TryRemove(roomId, out _);
    }

    private async Task Broadcast(LiveTable table, IEnumerable<string>? notices = null)
    {
        foreach (var player in table.State.Players)
        {
            var snap = Snapshot(table, player.UserId);
            await hub.Clients.Group($"user:{player.UserId}").SendAsync("gameUpdated", snap);
        }
        if (notices is not null)
        {
            foreach (var n in notices)
                await hub.Clients.Group($"room:{table.State.RoomId}").SendAsync("notice", n);
        }
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await db.Rooms.Include(r => r.Members).ThenInclude(m => m.User).Include(r => r.Games).ThenInclude(g => g.Players)
            .FirstOrDefaultAsync(r => r.Id == table.State.RoomId);
        if (room is not null)
        {
            foreach (var m in room.Members)
                await hub.Clients.Group($"user:{m.UserId}").SendAsync("roomUpdated", RoomService.ToDto(room, m.UserId));
        }
    }

    private LiveTable Require(Guid roomId)
    {
        if (!_tables.TryGetValue(roomId, out var table))
            throw new InvalidOperationException("No active game in this room.");
        return table;
    }

    private static int ActorSeat(GameState g) =>
        g.Phase == GamePhase.Selecting ? g.BidderSeat ?? g.CurrentTurn : g.CurrentTurn;

    private static void RefreshTurnDeadline(LiveTable table)
    {
        var g = table.State;
        if (g.Phase is GamePhase.Complete or GamePhase.Cancelled) return;
        var seat = ActorSeat(g);
        if (table.DeadlineSeat == seat && table.DeadlinePhase == g.Phase) return;
        table.DeadlineSeat = seat;
        table.DeadlinePhase = g.Phase;
        table.TurnDeadline = DateTime.UtcNow.Add(LiveTable.TurnLimit);
    }

    private static EngineResult? ExpireTurn(LiveTable table)
    {
        var g = table.State;
        if (g.Phase is GamePhase.Complete or GamePhase.Cancelled) return null;
        RefreshTurnDeadline(table);
        if (DateTime.UtcNow < table.TurnDeadline) return null;

        EngineResult result;
        string notice;
        if (g.Phase == GamePhase.Playing)
        {
            result = GameEngine.AutoPlayLowest(g, g.CurrentTurn);
            notice = "Time's up — the lowest legal card was played.";
        }
        else if (g.Phase == GamePhase.Bidding)
        {
            result = GameEngine.Pass(g, g.CurrentTurn);
            notice = "Time's up — passed.";
        }
        else if (g.Phase == GamePhase.Selecting)
        {
            var bidder = g.Seat(g.BidderSeat ?? 0);
            result = GameEngine.SelectTrumpAndPartners(
                g,
                bidder.Seat,
                GameEngine.SuggestBotTrump(bidder),
                GameEngine.SuggestBotConditions(g));
            notice = "Time's up — trump and partners were chosen automatically.";
        }
        else return null;

        if (!result.Ok)
        {
            table.TurnDeadline = DateTime.UtcNow.Add(LiveTable.TurnLimit);
            return null;
        }
        table.DeadlineSeat = -1;
        RefreshTurnDeadline(table);
        return new EngineResult
        {
            Ok = true,
            State = g,
            GameFinished = result.GameFinished,
            Notices = [.. result.Notices, notice]
        };
    }

    public static GameSnapshotDto Snapshot(LiveTable table, Guid viewerId)
    {
        var iso = table.State.Phase is GamePhase.Bidding or GamePhase.Selecting or GamePhase.Playing
            ? table.TurnDeadline.ToUniversalTime().ToString("O")
            : null;
        return Snapshot(table.State, viewerId, iso);
    }

    public static GameSnapshotDto Snapshot(GameState g, Guid viewerId, string? turnEndsAt = null)
    {
        var you = g.Players.FirstOrDefault(p => p.UserId == viewerId);
        var hidePoints = g.Phase is GamePhase.Playing or GamePhase.Bidding or GamePhase.Selecting;
        var playable = Array.Empty<CardDto>();
        if (you is not null && g.Phase == GamePhase.Playing && g.CurrentTurn == you.Seat)
            playable = CardRules.LegalCards(you.Hand, g.LeadSuit).Select(c => c.ToDto()).ToArray();

        return new GameSnapshotDto(
            g.GameId,
            g.RoomId,
            g.Phase.ToString().ToLowerInvariant(),
            g.DealerSeat,
            g.CurrentTurn,
            g.Bid,
            g.BidderSeat,
            g.HasAnyBid,
            g.Trump,
            g.Conditions.Select(c => new PartnerConditionDto(c.Nth, c.Rank, c.Suit)).ToList(),
            g.PartnerSeats,
            g.BidLog.Select(b => new BidLogDto(b.Seat, b.Kind, b.Amount)).ToList(),
            g.CurrentTrick.Select(t => new TrickPlayDto(t.Seat, t.Card.ToDto(), g.Seat(t.Seat).UserName)).ToList(),
            g.LeadSuit,
            Math.Min(g.TrickNumber, 13),
            hidePoints ? 0 : g.TeamPoints,
            g.Phase == GamePhase.Complete ? g.Success : null,
            g.Players.Select(p => new PublicSeatDto(
                p.UserId,
                p.UserName,
                p.Seat,
                p.IsBot,
                p.Hand.Count,
                hidePoints ? null : p.PointsWon,
                p.ScoreDelta,
                p.Seat == g.BidderSeat,
                g.PartnerSeats.Contains(p.Seat))).ToList(),
            you?.Hand.Select(c => c.ToDto()).ToList() ?? [],
            playable.ToList(),
            g.CancelReason,
            GameState.RuleVersion,
            turnEndsAt);
    }
}
