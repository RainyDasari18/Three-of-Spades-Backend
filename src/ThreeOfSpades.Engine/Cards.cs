namespace ThreeOfSpades.Engine;

public sealed record Card(string Id, string Rank, string Suit, int Deck);

public sealed record PartnerCondition(int Nth, string Rank, string Suit);

public sealed record BidAction(int Seat, string Kind, int? Amount);

public sealed record TrickPlay(int Seat, Card Card);

public sealed record CompletedTrick(int WinnerSeat, List<TrickPlay> Plays, int Points);

public static class Suits
{
    public static readonly string[] All = ["S", "H", "D", "C"];
}

public static class Ranks
{
    public static readonly string[] All = ["A", "K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2"];

    public static readonly Dictionary<string, int> Value = new()
    {
        ["A"] = 14, ["K"] = 13, ["Q"] = 12, ["J"] = 11, ["10"] = 10,
        ["9"] = 9, ["8"] = 8, ["7"] = 7, ["6"] = 6, ["5"] = 5, ["4"] = 4, ["3"] = 3, ["2"] = 2
    };
}

public static class CardRules
{
    public static int Points(Card card)
    {
        if (card.Rank == "3" && card.Suit == "S") return 30;
        if (card.Rank == "5") return 5;
        if (card.Rank is "10" or "J" or "Q" or "K" or "A") return 10;
        return 0;
    }

    public static int HandPoints(IEnumerable<Card> hand) => hand.Sum(Points);

    public static string Signature(string rank, string suit) => $"{rank}{suit}";

    public static int PartnerConditionCount(int playerCount) => playerCount switch
    {
        <= 5 => 1,
        6 => 2,
        7 => 3,
        _ => 4
    };

    public static List<Card> LegalCards(IReadOnlyList<Card> hand, string? leadSuit)
    {
        if (leadSuit is null) return hand.ToList();
        var follow = hand.Where(c => c.Suit == leadSuit).ToList();
        return follow.Count > 0 ? follow : hand.ToList();
    }

    public static Card LowestLegal(IReadOnlyList<Card> hand, string? leadSuit)
    {
        var legal = LegalCards(hand, leadSuit);
        var suitOrder = new[] { "C", "D", "H", "S" };
        return legal
            .OrderBy(c => Ranks.Value[c.Rank])
            .ThenBy(c => Array.IndexOf(suitOrder, c.Suit))
            .First();
    }

    public static int TrickWinner(IReadOnlyList<TrickPlay> plays, string trump, string leadSuit)
    {
        var trumps = plays.Where(p => p.Card.Suit == trump).ToList();
        var pool = trumps.Count > 0 ? trumps : plays.Where(p => p.Card.Suit == leadSuit).ToList();
        var best = pool[0];
        foreach (var play in pool)
        {
            var rv = Ranks.Value[play.Card.Rank];
            var bv = Ranks.Value[best.Card.Rank];
            if (rv > bv) best = play;
            else if (rv == bv && play.Card.Suit == best.Card.Suit && play.Seat != best.Seat)
                best = play;
        }
        return best.Seat;
    }
}
