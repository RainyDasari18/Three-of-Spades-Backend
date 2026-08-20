namespace ThreeOfSpades.Engine;

public sealed class EngineResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public required GameState State { get; init; }
    public List<string> Notices { get; init; } = [];
    public bool GameFinished { get; init; }
    public bool Cancelled { get; init; }

    public static EngineResult Success(GameState state, params string[] notices) =>
        new() { Ok = true, State = state, Notices = [.. notices] };

    public static EngineResult Fail(GameState state, string error) =>
        new() { Ok = false, Error = error, State = state };
}
