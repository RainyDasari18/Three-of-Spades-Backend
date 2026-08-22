using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ThreeOfSpades.Api.Contracts;
using ThreeOfSpades.Api.Data;
using ThreeOfSpades.Api.Domain;
using ThreeOfSpades.Api.Hubs;

namespace ThreeOfSpades.Api.Services;

public class RoomService(AppDbContext db, IHubContext<GameHub> hub)
{
    private static readonly string[] BotNames = ["Aisha", "Vikram", "Meera", "Arjun", "Priya", "Kabir", "Noor"];

    public async Task<List<RoomDto>> ListForUser(Guid userId, CancellationToken ct)
    {
        var rooms = await db.Rooms
            .Include(r => r.Members).ThenInclude(m => m.User)
            .Include(r => r.Games).ThenInclude(g => g.Players)
            .Where(r => r.Members.Any(m => m.UserId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return rooms.Select(r => ToDto(r, userId)).ToList();
    }

    public async Task<RoomDto> Create(Guid userId, string name, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct) ?? throw new InvalidOperationException("User not found.");
        var room = new Room
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New room" : name.Trim(),
            Code = await MakeUniqueCode(name, ct),
            OwnerId = userId
        };
        room.Members.Add(new RoomMember { UserId = userId, User = user, Ready = false, Online = true });
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return ToDto(room, userId);
    }

    public async Task<RoomDto> Join(Guid userId, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Room code is required.");
        var room = await LoadByCode(code, ct) ?? throw new InvalidOperationException("No room found for that code.");
        if (room.Archived) throw new InvalidOperationException("Room is archived.");
        var already = room.Members.Any(m => m.UserId == userId);
        if (!already)
        {
            if (room.ActiveGameId is not null) throw new InvalidOperationException("Cannot join after a game has started.");
            if (room.Members.Count >= 8) throw new InvalidOperationException("Room is full (max 8).");
            room.Members.Add(new RoomMember { RoomId = room.Id, UserId = userId, Ready = false, Online = true });
            await db.SaveChangesAsync(ct);
            await db.Entry(room).Collection(r => r.Members).Query().Include(m => m.User).LoadAsync(ct);
        }
        await NotifyRoom(room);
        return ToDto(room, userId);
    }

    public async Task<RoomDto> Get(Guid userId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureMember(room, userId);
        var member = room.Members.First(x => x.UserId == userId);
        if (!member.Online)
        {
            member.Online = true;
            await db.SaveChangesAsync(ct);
        }
        return ToDto(room, userId);
    }

    public async Task<RoomDto> ToggleReady(Guid userId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureMember(room, userId);
        if (room.Archived) throw new InvalidOperationException("Room is archived.");
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Game is already active.");
        var m = room.Members.First(x => x.UserId == userId);
        m.Ready = !m.Ready;
        m.Online = true;
        await db.SaveChangesAsync(ct);
        await NotifyRoom(room);
        return ToDto(room, userId);
    }

    public async Task SetOnline(Guid userId, Guid roomId, bool online, CancellationToken ct)
    {
        var member = await db.RoomMembers.FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, ct);
        if (member is null) return;
        member.Online = online;
        await db.SaveChangesAsync(ct);
    }

    public async Task<RoomDto> FillBots(Guid userId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureOwner(room, userId);
        if (room.Archived) throw new InvalidOperationException("Room is archived.");
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Game is already active.");
        foreach (var name in BotNames)
        {
            if (room.Members.Count >= 6) break;
            if (room.Members.Any(m => m.User.UserName == name || m.User.Email == $"bot-{name.ToLowerInvariant()}@spades.local"))
                continue;
            var email = $"bot-{name.ToLowerInvariant()}@spades.local";
            var bot = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsBot, ct);
            if (bot is null)
            {
                if (await db.Users.AnyAsync(u => u.Email == email, ct))
                    email = $"bot-{name.ToLowerInvariant()}-{room.Id.ToString("N")[..8]}@spades.local";
                bot = new User { Email = email, UserName = name, IsBot = true };
                db.Users.Add(bot);
            }
            bot.IsBot = true;
            room.Members.Add(new RoomMember { RoomId = room.Id, UserId = bot.Id, User = bot, Ready = true, Online = true });
        }
        await db.SaveChangesAsync(ct);
        var loaded = (await Load(roomId, ct))!;
        await NotifyRoom(loaded);
        return ToDto(loaded, userId);
    }

    public async Task<RoomDto> Kick(Guid ownerId, Guid roomId, Guid targetId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureOwner(room, ownerId);
        if (targetId == ownerId) throw new InvalidOperationException("Cannot kick yourself.");
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Cannot kick during a game.");
        var member = room.Members.FirstOrDefault(m => m.UserId == targetId)
                     ?? throw new InvalidOperationException("Not a member.");
        db.RoomMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        var loaded = (await Load(roomId, ct))!;
        await NotifyRoom(loaded);
        await hub.Clients.Group($"user:{targetId}").SendAsync("kickedFromRoom", roomId);
        return ToDto(loaded, ownerId);
    }

    public async Task<RoomDto> Transfer(Guid ownerId, Guid roomId, Guid targetId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureOwner(room, ownerId);
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Cannot transfer ownership during a game.");
        if (targetId == ownerId) throw new InvalidOperationException("You already own this room.");
        var member = room.Members.FirstOrDefault(m => m.UserId == targetId)
                     ?? throw new InvalidOperationException("Not a member.");
        if (IsDummy(member.User)) throw new InvalidOperationException("Cannot transfer ownership to a dummy player.");
        room.OwnerId = targetId;
        await db.SaveChangesAsync(ct);
        var loaded = (await Load(roomId, ct))!;
        await NotifyRoom(loaded);
        return ToDto(loaded, ownerId);
    }

    public async Task Archive(Guid ownerId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureOwner(room, ownerId);
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Cannot archive during a game.");
        room.Archived = true;
        await db.SaveChangesAsync(ct);
        await NotifyRoom(room);
    }

    public async Task Leave(Guid userId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        if (room.ActiveGameId is not null)
            throw new InvalidOperationException("You cannot leave while a game is active.");
        var member = room.Members.FirstOrDefault(m => m.UserId == userId)
                     ?? throw new InvalidOperationException("Not a member.");
        db.RoomMembers.Remove(member);
        var remaining = room.Members.Where(m => m.UserId != userId).ToList();
        if (room.OwnerId == userId)
        {
            var nextHuman = remaining.Where(m => !IsDummy(m.User)).OrderBy(m => m.JoinedAt).FirstOrDefault();
            if (nextHuman is null) room.Archived = true;
            else room.OwnerId = nextHuman.UserId;
        }
        await db.SaveChangesAsync(ct);
        if (remaining.Count > 0)
            await NotifyRoom((await Load(roomId, ct))!);
    }

    public async Task<Room> RequireStartable(Guid ownerId, Guid roomId, CancellationToken ct)
    {
        var room = await Load(roomId, ct) ?? throw new InvalidOperationException("Room not found.");
        EnsureOwner(room, ownerId);
        if (room.Archived) throw new InvalidOperationException("Room is archived.");
        if (room.ActiveGameId is not null) throw new InvalidOperationException("Game is already active.");
        var n = room.Members.Count;
        if (n is < 5 or > 8) throw new InvalidOperationException("Need 5–8 players to start.");
        if (room.Members.Any(m => !m.Online || !m.Ready))
            throw new InvalidOperationException("Every player must be online and ready.");
        return room;
    }

    public async Task<Room?> Load(Guid id, CancellationToken ct) =>
        await db.Rooms
            .Include(r => r.Members).ThenInclude(m => m.User)
            .Include(r => r.Games).ThenInclude(g => g.Players)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    private async Task NotifyRoom(Room room)
    {
        foreach (var m in room.Members)
            await hub.Clients.Group($"user:{m.UserId}").SendAsync("roomUpdated", ToDto(room, m.UserId));
        await hub.Clients.Group($"room:{room.Id}").SendAsync("roomUpdated", ToDto(room, room.OwnerId));
    }

    private async Task<Room?> LoadByCode(string code, CancellationToken ct) =>
        await db.Rooms
            .Include(r => r.Members).ThenInclude(m => m.User)
            .Include(r => r.Games).ThenInclude(g => g.Players)
            .FirstOrDefaultAsync(r => r.Code == code.Trim().ToUpperInvariant() && !r.Archived, ct);

    private static void EnsureMember(Room room, Guid userId)
    {
        if (room.Members.All(m => m.UserId != userId))
            throw new InvalidOperationException("Not a member of this room.");
    }

    private static void EnsureOwner(Room room, Guid userId)
    {
        if (room.OwnerId != userId) throw new InvalidOperationException("Only the room owner can do that.");
    }

    private async Task<string> MakeUniqueCode(string name, CancellationToken ct)
    {
        for (var i = 0; i < 24; i++)
        {
            var code = MakeCode(name);
            if (!await db.Rooms.AnyAsync(r => r.Code == code, ct))
                return code;
        }
        return $"R{Guid.NewGuid().ToString("N")[..7]}".ToUpperInvariant();
    }

    private static string MakeCode(string name)
    {
        var raw = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (raw.Length > 5) raw = raw[..5];
        if (string.IsNullOrEmpty(raw)) raw = "ROOM";
        return $"{raw}{Random.Shared.Next(10, 99)}";
    }

    public static bool IsDummy(User user) =>
        user.IsBot || user.Email.EndsWith("@spades.local", StringComparison.OrdinalIgnoreCase);

    public static RoomDto ToDto(Room room, Guid viewerId)
    {
        var members = room.Members
            .OrderBy(m => m.JoinedAt)
            .Select(m => new MemberDto(m.UserId, m.User.UserName, m.UserId == room.OwnerId, m.Online, m.Ready, IsDummy(m.User)))
            .ToList();

        var history = room.Games
            .OrderByDescending(g => g.PlayedAt)
            .Select(g =>
            {
                var you = g.Players.FirstOrDefault(p => p.UserId == viewerId);
                return new GameHistoryDto(g.Id, g.PlayedAt, g.PlayerCount, g.BidderName, g.Bid, g.Trump, g.Success, g.TeamPoints, you?.ScoreDelta ?? 0);
            })
            .ToList();

        var scores = new Dictionary<string, int>();
        var partnerScores = new Dictionary<string, int>();
        var bidMade = new Dictionary<string, int>();
        var bidFailed = new Dictionary<string, int>();
        foreach (var g in room.Games)
        {
            foreach (var p in g.Players)
            {
                scores[p.UserName] = scores.GetValueOrDefault(p.UserName) + p.ScoreDelta;
                if (p.IsPartner) partnerScores[p.UserName] = partnerScores.GetValueOrDefault(p.UserName) + p.ScoreDelta;
                if (p.IsBidder && g.Success) bidMade[p.UserName] = bidMade.GetValueOrDefault(p.UserName) + 1;
                if (p.IsBidder && !g.Success) bidFailed[p.UserName] = bidFailed.GetValueOrDefault(p.UserName) + 1;
            }
        }

        string Best(Dictionary<string, int> map) =>
            map.Count == 0 ? "—" : map.MaxBy(x => x.Value).Key;

        var stats = new RoomStatsDto(
            room.Games.Count,
            Best(bidMade),
            Best(bidFailed),
            Best(partnerScores),
            partnerScores.Count == 0 ? "—" : partnerScores.MinBy(x => x.Value).Key,
            scores.Select(kv => new LeaderRow(kv.Key, kv.Value)).OrderByDescending(x => x.Score).ToList());

        return new RoomDto(room.Id, room.Name, room.Code, room.Archived, room.OwnerId, room.ActiveGameId, members, history, stats);
    }
}
