using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acceptance.Tests.Support;

/// <summary>
/// Rapport de recette : chaque scénario Gherkin exécuté, ses étapes et OK/KO. Écrit à la fin des tests dans
/// artifacts/gherkin-report.json (à la racine du dépôt) ; la page admin « Gherkin » du site l'affiche.
/// Chemin modifiable par la variable d'environnement SOUQLY_GHERKIN_REPORT.
/// </summary>
[Binding]
public sealed class GherkinReport(ScenarioContext scenario, FeatureContext feature)
{
    private static readonly ConcurrentQueue<ScenarioResult> Results = new();
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    private readonly List<StepResult> _steps = [];
    private readonly Stopwatch _watch = new();

    [BeforeScenario(Order = int.MinValue)]
    public void Start() => _watch.Start();

    [AfterStep]
    public void RecordStep()
    {
        var step = scenario.StepContext.StepInfo;
        var error = scenario.TestError;
        _steps.Add(new StepResult(
            step.StepInstance?.Keyword?.Trim() ?? step.StepDefinitionType.ToString(),
            step.Text,
            scenario.ScenarioExecutionStatus == ScenarioExecutionStatus.OK,
            error is null ? null : Shorten(error.Message)));
    }

    [AfterScenario(Order = int.MaxValue)]
    public void RecordScenario()
    {
        _watch.Stop();
        var tags = scenario.ScenarioInfo.CombinedTags ?? [];
        Results.Enqueue(new ScenarioResult(
            tags.FirstOrDefault(t => t.StartsWith("US-", StringComparison.Ordinal)) ?? string.Empty,
            feature.FeatureInfo.Title,
            scenario.ScenarioInfo.Title,
            scenario.ScenarioExecutionStatus == ScenarioExecutionStatus.OK,
            scenario.ScenarioExecutionStatus.ToString(),
            (long)_watch.Elapsed.TotalMilliseconds,
            _steps.ToList(),
            scenario.TestError is null ? null : Shorten(scenario.TestError.Message)));
    }

    [AfterTestRun(Order = int.MaxValue)]
    public static void Write()
    {
        var path = Environment.GetEnvironmentVariable("SOUQLY_GHERKIN_REPORT") ?? DefaultPath();
        if (path is null)
        {
            return;
        }

        var scenarios = Results.OrderBy(r => r.Story, StringComparer.Ordinal).ThenBy(r => r.Feature).ThenBy(r => r.Scenario).ToList();
        var report = new Report(StartedAt, DateTimeOffset.UtcNow, Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ? "CI GitHub" : Environment.MachineName,
            scenarios.Count, scenarios.Count(s => s.Ok), scenarios.Count(s => !s.Ok), scenarios);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, Options));
    }

    /// <summary>artifacts/gherkin-report.json à la racine du dépôt (dossier qui contient Souqly.slnx).</summary>
    private static string? DefaultPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Souqly.slnx")))
            {
                return Path.Combine(dir.FullName, "artifacts", "gherkin-report.json");
            }
        }

        return null;
    }

    private static string Shorten(string message)
    {
        var line = message.ReplaceLineEndings(" ").Trim();
        return line.Length > 400 ? line[..400] + "…" : line;
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record Report(DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Machine, int Total, int Passed, int Failed, IReadOnlyList<ScenarioResult> Scenarios);

    private sealed record ScenarioResult(string Story, string Feature, string Scenario, bool Ok, string Status, long DurationMs, IReadOnlyList<StepResult> Steps, string? Error);

    private sealed record StepResult(string Keyword, string Text, bool Ok, string? Error);
}
