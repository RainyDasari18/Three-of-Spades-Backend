namespace ThreeOfSpades.Engine;

public static class GameEngine
{
    public static GameState DealNewGame(
        Guid roomId,
        IReadOnlyList<(Guid UserId, string UserName, bool IsBot)> seated,
        int dealerSeat,
        Random? rng = null)
    {
        var n = seated.Count;
        if (n is < 5 or > 8)
            throw new ArgumentException("Need 5–8 players.");

        var hands = DeckFactory.DealHands(n, rng);
        var players = seated.Select((p, i) => new SeatPlayer
        {
            UserId = p.UserId,
            UserName = p.UserName,
            Seat = i,
            IsBot = p.IsBot,
            Hand = hands[i]
        }).ToList();

        return new GameState
        {
            RoomId = roomId,
            Players = players,
            DealerSeat = dealerSeat,
            Phase = GamePhase.Bidding,
            CurrentTurn = (dealerSeat + 1) % n,
            ActiveDeck = DeckFactory.BuildActiveDeck(n)
        };
    }

    public static EngineResult Bid(GameState g, int seat, int amount)
    {
        if (g.Phase != GamePhase.Bidding) return EngineResult.Fail(g, "Not in bidding.");
        if (g.CurrentTurn != seat) return EngineResult.Fail(g, "Not your turn.");
        if (amount is < 100 or > 500) return EngineResult.Fail(g, "Bid must be between 100 and 500.");
        if (amount <= g.Bid) return EngineResult.Fail(g, "Bid must beat the current high bid.");

        g.Bid = amount;
        g.BidderSeat = seat;
        g.HasAnyBid = true;
        g.PassesSinceRaise = 0;
        g.CurrentTurn = (seat + 1) % g.PlayerCount;
        g.BidLog.Add(new BidAction(seat, "bid", amount));
        return EngineResult.Success(g);
    }

    public static EngineResult Pass(GameState g, int seat)
    {
        if (g.Phase != GamePhase.Bidding) return EngineResult.Fail(g, "Not in bidding.");
        if (g.CurrentTurn != seat) return EngineResult.Fail(g, "Not your turn.");

        var n = g.PlayerCount;
        g.BidLog.Add(new BidAction(seat, "pass", null));
        var passes = g.PassesSinceRaise + 1;

        if (!g.HasAnyBid && passes >= n)
        {
            var forced = (g.DealerSeat + 1) % n;
            g.Bid = 100;
            g.BidderSeat = forced;
            g.HasAnyBid = true;
            g.PassesSinceRaise = 0;
            g.CurrentTurn = forced;
            g.Phase = GamePhase.Selecting;
            return EngineResult.Success(g, $"{g.Seat(forced).UserName} is forced to 100.");
        }

        if (g.HasAnyBid && passes >= n - 1)
        {
            var winner = g.BidderSeat ?? seat;
            g.Phase = GamePhase.Selecting;
            g.CurrentTurn = winner;
            return EngineResult.Success(g, $"{g.Seat(winner).UserName} has the bid at {g.Bid}.");
        }

        g.PassesSinceRaise = passes;
        g.CurrentTurn = (seat + 1) % n;
        return EngineResult.Success(g);
    }

    public static EngineResult SelectTrumpAndPartners(GameState g, int seat, string trump, IReadOnlyList<PartnerCondition> conditions)
    {
        if (g.Phase != GamePhase.Selecting) return EngineResult.Fail(g, "Not selecting trump.");
        if (g.BidderSeat != seat) return EngineResult.Fail(g, "Only the bidder can select.");
        if (!Suits.All.Contains(trump)) return EngineResult.Fail(g, "Invalid trump.");

        var need = CardRules.PartnerConditionCount(g.PlayerCount);
        var unique = DedupPartnerConditions(g, conditions, need);
        if (unique.Count != need)
            return EngineResult.Fail(g, $"Select {need} partner condition(s).");

        foreach (var c in unique)
        {
            if (c.Nth is not (1 or 2)) return EngineResult.Fail(g, "Nth must be 1 or 2.");
            if (!DeckFactory.CardExists(g.ActiveDeck, c.Rank, c.Suit))
                return EngineResult.Fail(g, "That card is not in the active deck.");
            if (c.Nth == 2 && DeckFactory.Copies(g.ActiveDeck, c.Rank, c.Suit) < 2)
                return EngineResult.Fail(g, "There is no 2nd copy of that card.");
        }

        g.Trump = trump;
        g.Conditions = unique;
        g.Phase = GamePhase.Playing;
        var leader = g.BidderSeat ?? 0;
        g.CurrentTurn = leader;
        g.TrickLeader = leader;
        g.CurrentTrick = [];
        g.LeadSuit = null;
        g.TrickNumber = 1;
        return EngineResult.Success(g, $"Trump is {trump}. Partners stay hidden until played.");
    }

    public static EngineResult PlayCard(GameState g, int seat, string cardId)
    {
        if (g.Phase != GamePhase.Playing) return EngineResult.Fail(g, "Not playing.");
        if (g.CurrentTurn != seat) return EngineResult.Fail(g, "Not your turn.");

        var player = g.Seat(seat);
        var card = player.Hand.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return EngineResult.Fail(g, "Card not in hand.");

        var legal = CardRules.LegalCards(player.Hand, g.LeadSuit);
        if (legal.All(c => c.Id != card.Id))
            return EngineResult.Fail(g, "Must follow suit when possible.");

        var notices = new List<string>();
        var leadSuit = g.CurrentTrick.Count == 0 ? card.Suit : g.LeadSuit;
        g.CurrentTrick.Add(new TrickPlay(seat, card));
        player.Hand.RemoveAll(c => c.Id == card.Id);

        var sig = CardRules.Signature(card.Rank, card.Suit);
        g.PlayCounts.TryGetValue(sig, out var seen);
        seen++;
        g.PlayCounts[sig] = seen;
        var hit = g.Conditions.FirstOrDefault(c => c.Rank == card.Rank && c.Suit == card.Suit && c.Nth == seen);
        if (hit is not null && !g.PartnerSeats.Contains(seat))
        {
            g.PartnerSeats.Add(seat);
            notices.Add($"{player.UserName} is a partner ({hit.Nth}{(hit.Nth == 1 ? "st" : "nd")} {hit.Rank}{hit.Suit})");
        }

        if (g.CurrentTrick.Count < g.PlayerCount)
        {
            g.LeadSuit = leadSuit;
            g.CurrentTurn = (seat + 1) % g.PlayerCount;
            return EngineResult.Success(g, [.. notices]);
        }

        var winner = CardRules.TrickWinner(g.CurrentTrick, g.Trump!, leadSuit!);
        var points = g.CurrentTrick.Sum(p => CardRules.Points(p.Card));
        g.CompletedTricks.Add(new CompletedTrick(winner, [.. g.CurrentTrick], points));
        g.Seat(winner).PointsWon += points;
        g.CurrentTrick = [];
        g.LeadSuit = null;
        g.TrickLeader = winner;
        g.CurrentTurn = winner;
        g.TrickNumber++;

        if (g.TrickNumber > 13)
        {
            Score(g);
            notices.Add(g.Success == true ? "Bid made." : "Bid failed.");
            return new EngineResult { Ok = true, State = g, Notices = notices, GameFinished = true };
        }

        return EngineResult.Success(g, [.. notices]);
    }

    public static EngineResult AutoPlayLowest(GameState g, int seat)
    {
        if (g.Phase != GamePhase.Playing) return EngineResult.Fail(g, "Not playing.");
        var card = CardRules.LowestLegal(g.Seat(seat).Hand, g.LeadSuit);
        return PlayCard(g, seat, card.Id);
    }

    public static EngineResult Cancel(GameState g, string reason)
    {
        g.Phase = GamePhase.Cancelled;
        g.CancelReason = reason;
        return new EngineResult { Ok = true, State = g, Cancelled = true, Notices = [reason] };
    }

    public static void Score(GameState g)
    {
        var partners = g.PartnerSeats.Distinct().ToList();
        var bidder = g.BidderSeat ?? 0;
        var team = new HashSet<int> { bidder };
        foreach (var s in partners) team.Add(s);
        g.TeamPoints = g.Players.Where(p => team.Contains(p.Seat)).Sum(p => p.PointsWon);
        g.Success = g.TeamPoints >= g.Bid;

        foreach (var p in g.Players)
        {
            var isBidder = p.Seat == bidder;
            var isPartner = partners.Contains(p.Seat) && !isBidder;
            if (g.Success == true)
                p.ScoreDelta = isBidder ? 2 * g.Bid : isPartner ? g.Bid : 0;
            else if (isBidder) p.ScoreDelta = -g.Bid;
            else if (!isPartner) p.ScoreDelta = g.Bid;
            else p.ScoreDelta = 0;
        }
        g.Phase = GamePhase.Complete;
    }

    public static PartnerCondition[] SuggestBotConditions(GameState g)
    {
        var need = CardRules.PartnerConditionCount(g.PlayerCount);
        var options = new List<PartnerCondition>();
        foreach (var rank in new[] { "A", "K", "Q", "J", "10" })
        {
            foreach (var suit in Suits.All)
            {
                if (!DeckFactory.CardExists(g.ActiveDeck, rank, suit)) continue;
                options.Add(new PartnerCondition(1, rank, suit));
                if (DeckFactory.Copies(g.ActiveDeck, rank, suit) >= 2)
                    options.Add(new PartnerCondition(2, rank, suit));
            }
        }
        return options.Take(need).ToArray();
    }

    private static List<PartnerCondition> DedupPartnerConditions(
        GameState g,
        IReadOnlyList<PartnerCondition> requested,
        int need)
    {
        var used = new HashSet<string>();
        var unique = new List<PartnerCondition>();
        foreach (var c in requested)
        {
            var key = $"{c.Nth}{c.Rank}{c.Suit}";
            if (!used.Add(key)) continue;
            unique.Add(c);
            if (unique.Count == need) return unique;
        }
        foreach (var extra in SuggestBotConditions(g).Concat(
                     Suits.All.SelectMany(suit => new[] { "A", "K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2" }
                         .Select(rank => new PartnerCondition(1, rank, suit)))))
        {
            var key = $"{extra.Nth}{extra.Rank}{extra.Suit}";
            if (!used.Add(key)) continue;
            if (!DeckFactory.CardExists(g.ActiveDeck, extra.Rank, extra.Suit)) continue;
            unique.Add(extra);
            if (unique.Count == need) return unique;
        }
        return unique;
    }

    public static string SuggestBotTrump(SeatPlayer bidder)
    {
        return bidder.Hand
            .GroupBy(c => c.Suit)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .FirstOrDefault() ?? "S";
    }

    public static int? SuggestBotBid(SeatPlayer player, int currentBid, bool hasAnyBid)
    {
        var strength = CardRules.HandPoints(player.Hand);
        if (!hasAnyBid) return strength >= 55 ? 100 : null;
        if (strength >= 100 && currentBid <= 160) return Math.Min(500, currentBid + 20);
        if (strength >= 75 && currentBid < 140) return currentBid + 10;
        return null;
    }

    /// <summary>Play one dummy action. Returns false when a human must act or the hand is over.</summary>
    public static bool TakeBotAction(GameState g)
    {
        if (g.Phase is GamePhase.Complete or GamePhase.Cancelled) return false;
        if (g.Phase == GamePhase.Selecting)
        {
            var bidder = g.Seat(g.BidderSeat ?? 0);
            if (!bidder.IsBot) return false;
            var pick = SelectTrumpAndPartners(g, bidder.Seat, SuggestBotTrump(bidder), SuggestBotConditions(g));
            return pick.Ok;
        }

        var actor = g.Seat(g.CurrentTurn);
        if (!actor.IsBot) return false;

        if (g.Phase == GamePhase.Bidding)
        {
            var raise = SuggestBotBid(actor, g.Bid, g.HasAnyBid);
            if (raise is int amount)
            {
                var bid = Bid(g, actor.Seat, amount);
                if (bid.Ok) return true;
            }
            return Pass(g, actor.Seat).Ok;
        }

        if (g.Phase == GamePhase.Playing)
            return AutoPlayLowest(g, actor.Seat).Ok;

        return false;
    }

    public static void RunBots(GameState g)
    {
        for (var i = 0; i < 200; i++)
        {
            if (g.Phase == GamePhase.Playing)
            {
                TakeBotAction(g);
                return;
            }
            if (!TakeBotAction(g)) return;
        }
    }
}
