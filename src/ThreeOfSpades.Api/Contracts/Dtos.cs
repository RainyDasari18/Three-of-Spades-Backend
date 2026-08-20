using ThreeOfSpades.Engine;

namespace ThreeOfSpades.Api.Contracts;

public record RegisterRequest(string Email, string Password, string UserName);
public record LoginRequest(string Email, string Password);
public record SetUserNameRequest(string UserName);
public record AuthResponse(string Token, Guid UserId, string Email, string UserName, bool NeedsUserName);

public record CreateRoomRequest(string Name);
public record JoinRoomRequest(string Code);
public record TransferRequest(Guid UserId);
public record KickRequest(Guid UserId);
public record BidRequest(int Amount);
public record SelectRequest(string Trump, List<PartnerConditionDto>? Conditions);
public record PlayRequest(string CardId);
public record PartnerConditionDto(int Nth, string Rank, string Suit);

public record UserDto(Guid Id, string Email, string UserName);

public record MemberDto(
    Guid Id,
    string UserName,
    bool IsOwner,
    bool Online,
    bool Ready,
    bool IsBot);

public record GameHistoryDto(
    Guid Id,
    DateTime PlayedAt,
    int PlayerCount,
    string Bidder,
    int Bid,
    string Trump,
    bool Success,
    int TeamPoints,
    int YourScore);

public record LeaderRow(string Name, int Score);

public record RoomStatsDto(
    int GamesPlayed,
    string BestBidder,
    string WorstBidder,
    string BestBuddy,
    string WorstBuddy,
    List<LeaderRow> Leaderboard);

public record RoomDto(
    Guid Id,
    string Name,
    string Code,
    bool Archived,
    Guid OwnerId,
    Guid? ActiveGameId,
    List<MemberDto> Members,
    List<GameHistoryDto> History,
    RoomStatsDto Stats);

public record CardDto(string Id, string Rank, string Suit, int Deck);
public record BidLogDto(int Seat, string Kind, int? Amount);
public record TrickPlayDto(int Seat, CardDto Card, string UserName);

public record PublicSeatDto(
    Guid UserId,
    string UserName,
    int Seat,
    bool IsBot,
    int HandCount,
    int? PointsWon,
    int ScoreDelta,
    bool IsBidder,
    bool IsPartner);

public record GameSnapshotDto(
    Guid GameId,
    Guid RoomId,
    string Phase,
    int DealerSeat,
    int CurrentTurn,
    int Bid,
    int? BidderSeat,
    bool HasAnyBid,
    string? Trump,
    List<PartnerConditionDto> Conditions,
    List<int> PartnerSeats,
    List<BidLogDto> BidLog,
    List<TrickPlayDto> CurrentTrick,
    string? LeadSuit,
    int TrickNumber,
    int TeamPoints,
    bool? Success,
    List<PublicSeatDto> Players,
    List<CardDto> YourHand,
    List<CardDto> Playable,
    string? CancelReason,
    string RuleVersion);

public static class Mappers
{
    public static CardDto ToDto(this Card c) => new(c.Id, c.Rank, c.Suit, c.Deck);

    public static PartnerCondition ToModel(this PartnerConditionDto d) => new(d.Nth, d.Rank, d.Suit);
}
