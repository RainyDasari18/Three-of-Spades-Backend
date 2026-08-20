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
}
