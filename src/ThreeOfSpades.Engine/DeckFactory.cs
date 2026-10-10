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

    public static List<List<Card>> DealHands(
        int playerCount,
        Random? rng = null,
        int? forcedSeat = null,
        string? forcedSuit = null,
        int suitCardCount = 0)
    {
        rng ??= Random.Shared;
        var deck = BuildActiveDeck(playerCount);
        Shuffle(deck, rng);

        var hands = Enumerable.Range(0, playerCount).Select(_ => new List<Card>()).ToList();
        if (forcedSeat is int seat && !string.IsNullOrWhiteSpace(forcedSuit))
        {
            hands[seat] = TakeSuitHand(deck, forcedSuit, suitCardCount, rng);
            var others = Enumerable.Range(0, playerCount).Where(i => i != seat).ToList();
            for (var i = 0; i < deck.Count; i++)
                hands[others[i % others.Count]].Add(deck[i]);
        }
        else
        {
            for (var i = 0; i < deck.Count; i++)
                hands[i % playerCount].Add(deck[i]);
        }

        foreach (var hand in hands)
            SortHand(hand);
        return hands;
    }

    /// <summary>
    /// <paramref name="suitCardCount"/> cards of <paramref name="suit"/>: one ace, king, queen, and jack,
    /// then random cards of that suit for the rest. The other cards of the 13-card hand come from other suits.
    /// </summary>
    public static List<Card> TakeSuitHand(List<Card> deck, string suit, int suitCardCount, Random rng)
    {
        if (!Suits.All.Contains(suit))
            throw new InvalidOperationException("Suit must be S, H, D, or C.");
        if (suitCardCount is < 4 or > 13)
            throw new InvalidOperationException("Number of cards must be between 4 and 13.");

        var hand = new List<Card>(13);
        foreach (var rank in new[] { "A", "K", "Q", "J" })
        {
            var card = Take(deck, c => c.Rank == rank && c.Suit == suit)
                ?? throw new InvalidOperationException($"Cannot deal {rank}{suit}; it is not in the active deck.");
            hand.Add(card);
        }

        var extra = suitCardCount - 4;
        if (extra > 0)
        {
            var pool = deck.Where(c => c.Suit == suit).ToList();
            if (pool.Count < extra)
                throw new InvalidOperationException($"Not enough {suit} cards in the active deck.");
            Shuffle(pool, rng);
            foreach (var card in pool.Take(extra))
            {
                deck.Remove(card);
                hand.Add(card);
            }
        }

        var rest = 13 - hand.Count;
        for (var i = 0; i < rest; i++)
        {
            hand.Add(TakeRandom(deck, rng, c => c.Suit != suit)
                ?? throw new InvalidOperationException("Cannot fill the rest of the hand."));
        }
        return hand;
    }

    private static void Shuffle(List<Card> deck, Random rng)
    {
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    private static Card? Take(List<Card> deck, Func<Card, bool> match)
    {
        var index = deck.FindIndex(c => match(c));
        if (index < 0) return null;
        var card = deck[index];
        deck.RemoveAt(index);
        return card;
    }

    private static Card? TakeRandom(List<Card> deck, Random rng, Func<Card, bool> match)
    {
        var pool = deck.Where(match).ToList();
        if (pool.Count == 0) return null;
        var pick = pool[rng.Next(pool.Count)];
        deck.Remove(pick);
        return pick;
    }

    private static void SortHand(List<Card> hand)
    {
        hand.Sort((a, b) =>
        {
            var s = Array.IndexOf(Suits.All, a.Suit) - Array.IndexOf(Suits.All, b.Suit);
            return s != 0 ? s : Ranks.Value[b.Rank] - Ranks.Value[a.Rank];
        });
    }

    public static bool CardExists(IEnumerable<Card> deck, string rank, string suit) =>
        deck.Any(c => c.Rank == rank && c.Suit == suit);

    public static int Copies(IEnumerable<Card> deck, string rank, string suit) =>
        deck.Count(c => c.Rank == rank && c.Suit == suit);
}
