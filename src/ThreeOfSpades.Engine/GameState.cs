namespace ThreeOfSpades.Engine;

public enum GamePhase
{
    Bidding,
    Selecting,
    Playing,
    Complete,
    Cancelled
}

public sealed class SeatPlayer
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public int Seat { get; set; }
    public bool IsBot { get; set; }
    public List<Card> Hand { get; set; } = [];
    public int PointsWon { get; set; }
    public int ScoreDelta { get; set; }
}

public sealed class GameState
{
    public const string RuleVersion = "v1.0";

    public Guid GameId { get; set; } = Guid.NewGuid();
    public Guid RoomId { get; set; }
    public List<SeatPlayer> Players { get; set; } = [];
    public int DealerSeat { get; set; }
    public GamePhase Phase { get; set; } = GamePhase.Bidding;
    public int CurrentTurn { get; set; }
    public int Bid { get; set; }
    public int? BidderSeat { get; set; }
    public List<BidAction> BidLog { get; set; } = [];
    public int PassesSinceRaise { get; set; }
    public bool HasAnyBid { get; set; }
    public string? Trump { get; set; }
    public List<PartnerCondition> Conditions { get; set; } = [];
    public Dictionary<string, int> PlayCounts { get; set; } = new();
    public List<int> PartnerSeats { get; set; } = [];
    public List<TrickPlay> CurrentTrick { get; set; } = [];
    public string? LeadSuit { get; set; }
    public int TrickLeader { get; set; }
    public int TrickNumber { get; set; } = 1;
    public List<CompletedTrick> CompletedTricks { get; set; } = [];
    public int TeamPoints { get; set; }
    public bool? Success { get; set; }
    public List<Card> ActiveDeck { get; set; } = [];
    public string? CancelReason { get; set; }

    public int PlayerCount => Players.Count;
    public SeatPlayer Seat(int seat) => Players[seat];
}
