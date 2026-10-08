namespace ThreeOfSpades.Engine;

public static class FunHands
{
    public static readonly IReadOnlyList<(string Rank, string Suit)>[] All =
    [
        [
            ("A", "S"), ("A", "S"), ("K", "S"), ("Q", "S"), ("J", "S"), ("J", "S"), ("8", "S"), ("7", "S"), ("3", "S"),
            ("A", "D"), ("10", "D"), ("5", "D"),
            ("K", "H")
        ],
        [
            ("A", "C"), ("A", "C"), ("K", "C"), ("Q", "C"), ("Q", "C"), ("J", "C"), ("8", "C"), ("7", "C"), ("10", "C"),
            ("A", "H"), ("K", "H"), ("10", "H"),
            ("K", "D")
        ]
    ];

    public static IReadOnlyList<(string Rank, string Suit)> Pick(Random rng) => All[rng.Next(All.Length)];
}

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
        IReadOnlyList<(string Rank, string Suit)>? forcedPattern = null)
    {
        rng ??= Random.Shared;
        var deck = BuildActiveDeck(playerCount);
        Shuffle(deck, rng);

        var hands = Enumerable.Range(0, playerCount).Select(_ => new List<Card>()).ToList();
        if (forcedSeat is int seat && forcedPattern is { Count: 13 })
        {
            hands[seat] = TakeForcedHand(deck, forcedPattern, rng);
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
    /// Face cards and every other listed rank are taken as written.
    /// A 6 or 7 that is not in the active deck is replaced with a random card of the same suit.
    /// </summary>
    public static List<Card> TakeForcedHand(
        List<Card> deck,
        IReadOnlyList<(string Rank, string Suit)> pattern,
        Random rng)
    {
        var hand = new List<Card>(13);
        var flexibleSuits = new List<string>();
        foreach (var (rank, suit) in pattern)
        {
            if (rank is "6" or "7")
            {
                var exact = Take(deck, c => c.Rank == rank && c.Suit == suit);
                if (exact is not null) hand.Add(exact);
                else flexibleSuits.Add(suit);
                continue;
            }

            var card = Take(deck, c => c.Rank == rank && c.Suit == suit)
                ?? throw new InvalidOperationException($"Cannot deal {rank}{suit}; it is not in the active deck.");
            hand.Add(card);
        }

        foreach (var suit in flexibleSuits)
        {
            var sameSuit = deck.Where(c => c.Suit == suit).ToList();
            var pool = sameSuit.Count > 0 ? sameSuit : deck;
            var pick = pool[rng.Next(pool.Count)];
            deck.Remove(pick);
            hand.Add(pick);
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
