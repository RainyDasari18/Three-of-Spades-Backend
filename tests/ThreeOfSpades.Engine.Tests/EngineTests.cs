using ThreeOfSpades.Engine;

namespace ThreeOfSpades.Engine.Tests;

public class DeckTests
{
    [Theory]
    [InlineData(5, 65)]
    [InlineData(6, 78)]
    [InlineData(7, 91)]
    [InlineData(8, 104)]
    public void Active_deck_matches_player_count(int players, int cards)
    {
        var deck = DeckFactory.BuildActiveDeck(players);
        Assert.Equal(cards, deck.Count);
        Assert.Equal(2, deck.Count(c => c.Rank == "3" && c.Suit == "S"));
    }

    [Fact]
    public void Deal_gives_thirteen_each()
    {
        var hands = DeckFactory.DealHands(6, new Random(1));
        Assert.All(hands, h => Assert.Equal(13, h.Count));
    }
}

public class BiddingTests
{
    private static GameState Table()
    {
        var seated = Enumerable.Range(0, 6)
            .Select(i => (Guid.NewGuid(), $"P{i}", false))
            .ToList();
        return GameEngine.DealNewGame(Guid.NewGuid(), seated, 0, new Random(2));
    }

    [Fact]
    public void High_bidder_wins_when_everyone_else_passes()
    {
        var g = Table();
        var first = g.CurrentTurn;
        Assert.True(GameEngine.Bid(g, first, 140).Ok);
        for (var i = 0; i < 5; i++)
        {
            var r = GameEngine.Pass(g, g.CurrentTurn);
            Assert.True(r.Ok);
        }
        Assert.Equal(GamePhase.Selecting, g.Phase);
        Assert.Equal(first, g.BidderSeat);
        Assert.Equal(140, g.Bid);
    }

    [Fact]
    public void All_pass_forces_one_hundred()
    {
        var g = Table();
        for (var i = 0; i < 6; i++)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        Assert.Equal(GamePhase.Selecting, g.Phase);
        Assert.Equal(100, g.Bid);
        Assert.Equal((g.DealerSeat + 1) % 6, g.BidderSeat);
    }
}

public class BotTests
{
    [Fact]
    public void Dummy_players_bid_until_the_human_turn()
    {
        var human = Guid.NewGuid();
        var seated = new List<(Guid, string, bool)> { (human, "You", false) };
        for (var i = 0; i < 5; i++)
            seated.Add((Guid.NewGuid(), $"Bot{i}", true));
        var g = GameEngine.DealNewGame(Guid.NewGuid(), seated, 0, new Random(7));
        Assert.True(g.Seat(g.CurrentTurn).IsBot);
        GameEngine.RunBots(g);
        Assert.Equal(GamePhase.Bidding, g.Phase);
        Assert.Equal(0, g.CurrentTurn);
        Assert.False(g.Seat(g.CurrentTurn).IsBot);
        Assert.NotEmpty(g.BidLog);
    }

    [Fact]
    public void Human_lead_stays_on_the_table_while_dummies_follow_one_at_a_time()
    {
        var human = Guid.NewGuid();
        var seated = new List<(Guid, string, bool)> { (human, "You", false) };
        for (var i = 0; i < 5; i++)
            seated.Add((Guid.NewGuid(), $"Bot{i}", true));
        var g = GameEngine.DealNewGame(Guid.NewGuid(), seated, 0, new Random(7));
        GameEngine.RunBots(g);
        var amount = g.HasAnyBid ? Math.Min(500, g.Bid + 10) : 100;
        Assert.True(GameEngine.Bid(g, 0, amount).Ok);
        while (g.Phase == GamePhase.Bidding)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        Assert.Equal(GamePhase.Selecting, g.Phase);
        Assert.True(GameEngine.SelectTrumpAndPartners(g, 0, "S", GameEngine.SuggestBotConditions(g)).Ok);
        GameEngine.RunBots(g);
        Assert.Equal(GamePhase.Playing, g.Phase);
        Assert.Equal(0, g.CurrentTurn);
        Assert.Empty(g.CurrentTrick);
        var lead = g.Seat(0).Hand[0];
        Assert.True(GameEngine.PlayCard(g, 0, lead.Id).Ok);
        GameEngine.RunBots(g);
        Assert.Equal(2, g.CurrentTrick.Count);
        Assert.Equal(lead.Id, g.CurrentTrick[0].Card.Id);
    }

    [Fact]
    public void Duplicate_partner_picks_are_replaced_instead_of_rejected()
    {
        var seated = Enumerable.Range(0, 6)
            .Select(i => (Guid.NewGuid(), $"P{i}", i > 0))
            .ToList();
        var g = GameEngine.DealNewGame(Guid.NewGuid(), seated, 0, new Random(3));
        Assert.True(GameEngine.Bid(g, g.CurrentTurn, 100).Ok);
        while (g.Phase == GamePhase.Bidding)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        var bidder = g.BidderSeat ?? 0;
        var dup = new PartnerCondition[]
        {
            new(1, "A", "S"),
            new(1, "A", "S"),
        };
        Assert.True(GameEngine.SelectTrumpAndPartners(g, bidder, "S", dup).Ok);
        Assert.Equal(2, g.Conditions.Distinct().Count());
        Assert.Equal(GamePhase.Playing, g.Phase);
    }
}

public class ScoringTests
{
    [Fact]
    public void Success_pays_bidder_double_and_partners_once()
    {
        var g = new GameState
        {
            Bid = 100,
            BidderSeat = 0,
            PartnerSeats = [1],
            Players =
            [
                new SeatPlayer { Seat = 0, PointsWon = 80, UserName = "A" },
                new SeatPlayer { Seat = 1, PointsWon = 40, UserName = "B" },
                new SeatPlayer { Seat = 2, PointsWon = 20, UserName = "C" },
                new SeatPlayer { Seat = 3, PointsWon = 20, UserName = "D" },
                new SeatPlayer { Seat = 4, PointsWon = 20, UserName = "E" }
            ]
        };
        GameEngine.Score(g);
        Assert.True(g.Success);
        Assert.Equal(200, g.Players[0].ScoreDelta);
        Assert.Equal(100, g.Players[1].ScoreDelta);
        Assert.Equal(0, g.Players[2].ScoreDelta);
    }

    [Fact]
    public void Failure_penalizes_bidder_pays_others_and_zeros_partners()
    {
        var g = new GameState
        {
            Bid = 140,
            BidderSeat = 0,
            PartnerSeats = [1],
            Players =
            [
                new SeatPlayer { Seat = 0, PointsWon = 40, UserName = "A" },
                new SeatPlayer { Seat = 1, PointsWon = 20, UserName = "B" },
                new SeatPlayer { Seat = 2, PointsWon = 80, UserName = "C" },
                new SeatPlayer { Seat = 3, PointsWon = 80, UserName = "D" },
                new SeatPlayer { Seat = 4, PointsWon = 80, UserName = "E" }
            ]
        };
        GameEngine.Score(g);
        Assert.False(g.Success);
        Assert.Equal(-140, g.Players[0].ScoreDelta);
        Assert.Equal(0, g.Players[1].ScoreDelta);
        Assert.Equal(140, g.Players[2].ScoreDelta);
    }

    [Fact]
    public void Self_partner_does_not_pay_bidder_a_second_share()
    {
        var g = new GameState
        {
            Bid = 100,
            BidderSeat = 0,
            PartnerSeats = [0],
            Players =
            [
                new SeatPlayer { Seat = 0, PointsWon = 120, UserName = "A" },
                new SeatPlayer { Seat = 1, PointsWon = 20, UserName = "B" },
                new SeatPlayer { Seat = 2, PointsWon = 20, UserName = "C" },
                new SeatPlayer { Seat = 3, PointsWon = 20, UserName = "D" },
                new SeatPlayer { Seat = 4, PointsWon = 20, UserName = "E" }
            ]
        };
        GameEngine.Score(g);
        Assert.True(g.Success);
        Assert.Equal(200, g.Players[0].ScoreDelta);
        Assert.Equal(0, g.Players[1].ScoreDelta);
    }
}

public class NegativePlayTests
{
    private static GameState Table(int players = 6)
    {
        var seated = Enumerable.Range(0, players)
            .Select(i => (Guid.NewGuid(), $"P{i}", false))
            .ToList();
        return GameEngine.DealNewGame(Guid.NewGuid(), seated, 0, new Random(4));
    }

    [Fact]
    public void Rejects_too_few_players()
    {
        var seated = Enumerable.Range(0, 4).Select(i => (Guid.NewGuid(), $"P{i}", false)).ToList();
        Assert.Throws<ArgumentException>(() => GameEngine.DealNewGame(Guid.NewGuid(), seated, 0));
    }

    [Fact]
    public void Bid_rejects_wrong_turn_range_and_phase()
    {
        var g = Table();
        var turn = g.CurrentTurn;
        Assert.False(GameEngine.Bid(g, (turn + 1) % 6, 100).Ok);
        Assert.False(GameEngine.Bid(g, turn, 90).Ok);
        Assert.False(GameEngine.Bid(g, turn, 501).Ok);
        Assert.True(GameEngine.Bid(g, turn, 100).Ok);
        Assert.False(GameEngine.Bid(g, g.CurrentTurn, 100).Ok);
        Assert.True(GameEngine.Bid(g, g.CurrentTurn, 110).Ok);
        g.Phase = GamePhase.Playing;
        Assert.False(GameEngine.Bid(g, g.CurrentTurn, 200).Ok);
        Assert.False(GameEngine.Pass(g, g.CurrentTurn).Ok);
    }

    [Fact]
    public void Pass_then_reenter_is_allowed()
    {
        var g = Table();
        var first = g.CurrentTurn;
        Assert.True(GameEngine.Pass(g, first).Ok);
        Assert.True(GameEngine.Bid(g, g.CurrentTurn, 100).Ok);
        while (g.CurrentTurn != first)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        Assert.Equal(GamePhase.Bidding, g.Phase);
        Assert.True(GameEngine.Bid(g, first, 110).Ok);
    }

    [Fact]
    public void Select_rejects_non_bidder_bad_trump_and_missing_second_copy()
    {
        var g = Table(5);
        Assert.True(GameEngine.Bid(g, g.CurrentTurn, 100).Ok);
        while (g.Phase == GamePhase.Bidding)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        var bidder = g.BidderSeat ?? 0;
        Assert.False(GameEngine.SelectTrumpAndPartners(g, (bidder + 1) % 5, "S", [new(1, "A", "S")]).Ok);
        Assert.False(GameEngine.SelectTrumpAndPartners(g, bidder, "X", [new(1, "A", "S")]).Ok);
        var missing = g.ActiveDeck
            .GroupBy(c => (c.Rank, c.Suit))
            .First(x => x.Count() == 1);
        Assert.False(GameEngine.SelectTrumpAndPartners(g, bidder, "S", [new(2, missing.Key.Rank, missing.Key.Suit)]).Ok);
        Assert.True(GameEngine.SelectTrumpAndPartners(g, bidder, "S", [new(1, "A", "S")]).Ok);
    }

    [Fact]
    public void Play_rejects_off_turn_missing_card_and_renege()
    {
        var g = Table(5);
        Assert.True(GameEngine.Bid(g, g.CurrentTurn, 100).Ok);
        while (g.Phase == GamePhase.Bidding)
            Assert.True(GameEngine.Pass(g, g.CurrentTurn).Ok);
        var bidder = g.BidderSeat ?? 0;
        Assert.True(GameEngine.SelectTrumpAndPartners(g, bidder, "S", [new(1, "A", "S")]).Ok);

        Assert.False(GameEngine.PlayCard(g, (bidder + 1) % 5, g.Seat((bidder + 1) % 5).Hand[0].Id).Ok);
        Assert.False(GameEngine.PlayCard(g, bidder, "not-a-card").Ok);

        var lead = g.Seat(bidder).Hand[0];
        Assert.True(GameEngine.PlayCard(g, bidder, lead.Id).Ok);

        var follower = g.CurrentTurn;
        var mustFollow = g.Seat(follower).Hand.Where(c => c.Suit == lead.Suit).ToList();
        if (mustFollow.Count == 0) return;
        var renege = g.Seat(follower).Hand.FirstOrDefault(c => c.Suit != lead.Suit);
        if (renege is null) return;
        Assert.False(GameEngine.PlayCard(g, follower, renege.Id).Ok);
        Assert.True(GameEngine.PlayCard(g, follower, mustFollow[0].Id).Ok);
    }

    [Fact]
    public void Later_identical_copy_wins_the_trick()
    {
        var first = new Card("0-AS", "A", "S", 0);
        var later = new Card("1-AS", "A", "S", 1);
        var plays = new List<TrickPlay>
        {
            new(0, first),
            new(1, later),
            new(2, new Card("0-2S", "2", "S", 0)),
        };
        Assert.Equal(1, CardRules.TrickWinner(plays, "H", "S"));
    }

    [Fact]
    public void Active_deck_keeps_five_hundred_points()
    {
        foreach (var n in new[] { 5, 6, 7, 8 })
            Assert.Equal(500, DeckFactory.BuildActiveDeck(n).Sum(CardRules.Points));
    }

    [Fact]
    public void Auto_play_fails_on_empty_hand()
    {
        var g = Table(5);
        g.Phase = GamePhase.Playing;
        g.Seat(g.CurrentTurn).Hand.Clear();
        Assert.False(GameEngine.AutoPlayLowest(g, g.CurrentTurn).Ok);
    }
}
