namespace ThreeOfSpades.Engine;

public static class DeckFactory
{
    public static List<Card> TwoDecks()
    {
        var cards = new List<Card>();
        for (var deck = 0; deck <= 1; deck++)
        {
            foreach (var suit in Suits.All)
            {
                foreach (var rank in Ranks.All)
                    cards.Add(new Card($"{deck}-{rank}{suit}", rank, suit, deck));
            }
        }
        return cards;
    }

    public static List<Card> BuildActiveDeck(int playerCount)
    {
        var needed = playerCount * 13;
        var cards = TwoDecks();
        var removeOrder = new[] { "2", "3", "4", "6", "7", "8", "9" };
        var suitOrder = new[] { "C", "D", "H", "S" };

        foreach (var rank in removeOrder)
        {
            if (cards.Count <= needed) break;
            var victims = cards
                .Where(c => c.Rank == rank && !(rank == "3" && c.Suit == "S"))
                .OrderBy(c => Array.IndexOf(suitOrder, c.Suit))
                .ThenBy(c => c.Deck)
                .ToList();
            var toDrop = Math.Min(victims.Count, cards.Count - needed);
            var dropIds = victims.Take(toDrop).Select(c => c.Id).ToHashSet();
            cards = cards.Where(c => !dropIds.Contains(c.Id)).ToList();
        }
        return cards;
    }

    public static List<List<Card>> DealHands(int playerCount, Random? rng = null)
    {
        rng ??= Random.Shared;
        var deck = BuildActiveDeck(playerCount);
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        var hands = Enumerable.Range(0, playerCount).Select(_ => new List<Card>()).ToList();
        for (var i = 0; i < deck.Count; i++)
            hands[i % playerCount].Add(deck[i]);

        foreach (var hand in hands)
        {
            hand.Sort((a, b) =>
            {
                var s = Array.IndexOf(Suits.All, a.Suit) - Array.IndexOf(Suits.All, b.Suit);
                return s != 0 ? s : Ranks.Value[b.Rank] - Ranks.Value[a.Rank];
            });
        }
        return hands;
    }

    public static bool CardExists(IEnumerable<Card> deck, string rank, string suit) =>
        deck.Any(c => c.Rank == rank && c.Suit == suit);

    public static int Copies(IEnumerable<Card> deck, string rank, string suit) =>
        deck.Count(c => c.Rank == rank && c.Suit == suit);
}
