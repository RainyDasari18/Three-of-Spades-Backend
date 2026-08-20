namespace ThreeOfSpades.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string? GitHubId { get; set; }
    public bool IsBot { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public bool Archived { get; set; }
    public Guid? ActiveGameId { get; set; }
    public int DealerIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<RoomMember> Members { get; set; } = [];
    public List<GameRecord> Games { get; set; } = [];
}

public class RoomMember
{
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public bool Ready { get; set; }
    public bool Online { get; set; }
}

public class GameRecord
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public string RuleVersion { get; set; } = "v1.0";
    public int PlayerCount { get; set; }
    public int Bid { get; set; }
    public string Trump { get; set; } = "S";
    public string BidderName { get; set; } = "";
    public bool Success { get; set; }
    public int TeamPoints { get; set; }
    public string PartnerConditionsJson { get; set; } = "[]";
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public List<GamePlayerRecord> Players { get; set; } = [];
}

public class GamePlayerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public GameRecord Game { get; set; } = null!;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public int Seat { get; set; }
    public bool IsBidder { get; set; }
    public bool IsPartner { get; set; }
    public int PointsWon { get; set; }
    public int ScoreDelta { get; set; }
}
