using AgenticLab.Examples.Windfarm.Process;

namespace AgenticLab.Examples.Windfarm.Data;

internal static class WindfarmFixtures
{
    private static readonly DateTimeOffset ScenarioTime = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, Scenario> Scenarios = new Dictionary<string, Scenario>(StringComparer.Ordinal)
    {
        ["inspection"] = Build("inspection", "Gearbox inspection", false, false),
        ["replanning"] = Build("replanning", "Storm and delayed parts", true, false),
        ["missing-evidence"] = Build("missing-evidence", "Stale telemetry", false, true),
    };

    public static IReadOnlyList<ScenarioSummary> List() => Scenarios.Values.Select(scenario => scenario.Summary).ToArray();
    public static Scenario Get(string id) => !string.IsNullOrWhiteSpace(id) && Scenarios.TryGetValue(id, out var scenario)
        ? scenario : throw new WindfarmException(404, "Unknown scenario.");

    private static Scenario Build(string id, string name, bool storm, bool stale)
    {
        EvidenceSource Source(string kind, string system) => new($"{id}:{kind}:v1", system, ScenarioTime.AddMinutes(-20));
        return new(
            new(id, name, "WT-07 gearbox temperature and vibration trend alarm", "WT-07", ScenarioTime),
            new(Source("telemetry", "Synthetic condition monitoring") with { ObservedAt = stale ? ScenarioTime.AddDays(-2) : ScenarioTime.AddMinutes(-10) },
                "WT-07", 94, 8.2, stale ? 0 : 18),
            new(Source("history", "Synthetic maintenance system"), "WT-07",
                "Oil filter inspected 30 days ago. Intermittent pressure alarm noted; cause remains unconfirmed. No equipment action authorized."),
            [
                new("early", Source("weather-early", "Synthetic forecast"), ScenarioTime.AddHours(4), ScenarioTime.AddHours(7), storm ? 18 : 8, 2.1m),
                new("later", Source("weather-later", "Synthetic forecast"), ScenarioTime.AddHours(24), ScenarioTime.AddHours(27), 9, 1.4m),
            ],
            [
                new(Source("crew-alpha", "Synthetic workforce roster"), "crew-alpha", ["gearbox-inspection"], ScenarioTime.AddHours(2), ScenarioTime.AddHours(30)),
                new(Source("crew-bravo", "Synthetic workforce roster"), "crew-bravo", ["electrical-inspection"], ScenarioTime, ScenarioTime.AddHours(30)),
            ],
            [new(Source("parts", "Synthetic inventory"), "inspection-kit", 2, ScenarioTime.AddHours(storm ? 10 : 1))],
            new(Source("procedure", "Illustrative training procedure"), "WF-INSPECT-01",
                "Inspect gearbox lubrication and collect an oil sample; assess findings before further work",
                "gearbox-inspection", "inspection-kit", 1, 12, 3));
    }
}