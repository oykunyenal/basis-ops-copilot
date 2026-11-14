namespace FakeSap.Simulation;

/// <summary>The simulated landscape: a three-system track DEV → QAS → PRD.</summary>
internal sealed class SapLandscape
{
    private readonly Dictionary<string, SapSystemState> _systems;

    public SapLandscape(TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow();
        SapSystemState[] systems =
        [
            new("DEV", SystemRole.Development, seed: 3_001, networkOctet: 30, now),
            new("QAS", SystemRole.Quality, seed: 2_001, networkOctet: 20, now),
            new("PRD", SystemRole.Production, seed: 1_001, networkOctet: 10, now),
        ];
        _systems = systems.ToDictionary(s => s.Sid, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<SapSystemState> Systems => _systems.Values;

    public SapSystemState? Find(string sid) => _systems.GetValueOrDefault(sid);
}
