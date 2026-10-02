using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Getcmd.Cli.Commands;
using Getcmd.Cli.Services;

namespace Getcmd.Cli.Tests;

// Every test here points GETCMD_HOME at its own temp directory. Environment
// variables are process-wide, so all of them live in this one class: xUnit
// runs the tests of a class one at a time.
public sealed class CliTests : IDisposable
{
    private const string SessionId = "ef9e2abb-c7e4-464e-8768-aa5c37320898";
    private const string Cwd = "/home/ally/src/app";

    private readonly string _home = Directory.CreateTempSubdirectory("getcmd-test-").FullName;

    public CliTests()
    {
        Environment.SetEnvironmentVariable("GETCMD_HOME", _home);
        Environment.SetEnvironmentVariable("GETCMD_DISABLE", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GETCMD_HOME", null);
        Environment.SetEnvironmentVariable("GETCMD_DISABLE", null);
        Directory.Delete(_home, recursive: true);
    }

    private static (int Exit, string Out, string Err) Run(string? stdin, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = Program.Run(args, () => new MemoryStream(Encoding.UTF8.GetBytes(stdin ?? "")), stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static (int Exit, string Out, string Err) RunHook(string stdin) => Run(stdin, "hook", "claude");

    // Same shape as a real Claude Code PreToolUse payload.
    private static string Payload(string? command, string toolName = "Bash")
    {
        var toolInput = new JsonObject { ["description"] = "test" };
        if (command is not null)
        {
            toolInput["command"] = command;
        }

        return new JsonObject
        {
            ["session_id"] = SessionId,
            ["transcript_path"] = "/home/ally/.claude/projects/app/session.jsonl",
            ["cwd"] = Cwd,
            ["permission_mode"] = "auto",
            ["hook_event_name"] = "PreToolUse",
            ["tool_name"] = toolName,
            ["tool_input"] = toolInput,
            ["tool_use_id"] = "toolu_013QRB7HNYusSt6sXaSis3Rs",
        }.ToJsonString();
    }

    private List<LogEntry> LogRows()
    {
        using var log = DecisionLog.Open(new AppPaths(_home));
        return log.Query(100, null, null);
    }

    [Fact]
    public void HookAllowExitsZeroSilently()
    {
        var (exit, stdout, stderr) = RunHook(Payload("ls -la"));

        Assert.Equal(0, exit);
        Assert.Empty(stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void HookAskPrintsPermissionDecision()
    {
        var (exit, stdout, stderr) = RunHook(Payload("npm publish"));

        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.DoesNotContain('\n', stdout.TrimEnd());

        var output = JsonNode.Parse(stdout)!["hookSpecificOutput"]!;
        Assert.Equal("PreToolUse", (string?)output["hookEventName"]);
        Assert.Equal("ask", (string?)output["permissionDecision"]);
        Assert.Equal("getcmd: egress — Publishes a package", (string?)output["permissionDecisionReason"]);
    }

    [Fact]
    public void HookBlockExitsTwoWithReasonOnStderr()
    {
        var (exit, stdout, stderr) = RunHook(Payload("rm -rf ./build && git push --force"));

        Assert.Equal(2, exit);
        Assert.Empty(stdout);
        Assert.Equal(
            "getcmd blocked (destructive, git-force-push): Force push can overwrite remote history; "
            + "use --force-with-lease. Use git push --force-with-lease.",
            stderr.TrimEnd());
    }

    [Fact]
    public void HookWritesLogRow()
    {
        RunHook(Payload("git reset --hard"));

        var row = Assert.Single(LogRows());
        Assert.Equal("claude", row.Agent);
        Assert.Equal(SessionId, row.SessionId);
        Assert.Equal(Cwd, row.Cwd);
        Assert.Equal("git reset --hard", row.Command);
        Assert.Equal("destructive", row.Level);
        Assert.Equal("block", row.Action);
        Assert.Equal("git-reset-hard", row.RuleId);
        Assert.Equal("git reset --hard discards uncommitted changes", row.Reason);
        Assert.True(row.DurationMs >= 0);
        Assert.True(DateTime.TryParse(row.Ts, out _));
    }

    [Fact]
    public void HookLogsAllowedCommandsToo()
    {
        RunHook(Payload("ls -la"));

        var row = Assert.Single(LogRows());
        Assert.Equal("read", row.Level);
        Assert.Equal("allow", row.Action);
        Assert.Null(row.RuleId);
    }

    [Fact]
    public void HookIgnoresOtherTools()
    {
        var (exit, stdout, stderr) = RunHook(Payload("git reset --hard", toolName: "Write"));

        Assert.Equal(0, exit);
        Assert.Empty(stdout);
        Assert.Empty(stderr);
        Assert.False(File.Exists(Path.Combine(_home, "log.db")));
    }

    [Fact]
    public void HookIgnoresPayloadWithoutCommand()
    {
        Assert.Equal(0, RunHook(Payload(null)).Exit);
        Assert.Equal(0, RunHook("{}").Exit);
        Assert.False(File.Exists(Path.Combine(_home, "log.db")));
    }

    [Fact]
    public void HookDisabledByEnvironment()
    {
        Environment.SetEnvironmentVariable("GETCMD_DISABLE", "1");

        var (exit, _, stderr) = RunHook(Payload("git reset --hard"));

        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.False(File.Exists(Path.Combine(_home, "log.db")));
    }

    [Fact]
    public void HookFailureIsLoggedAndNeverBreaksTheAgent()
    {
        var (exit, stdout, stderr) = RunHook("this is not json");

        Assert.Equal(0, exit);
        Assert.Empty(stdout);
        Assert.Empty(stderr);
        Assert.Contains("JsonException", File.ReadAllText(Path.Combine(_home, "hook-errors.log")));
    }

    [Fact]
    public void HookWithBrokenConfigExitsZeroAndLogsTheError()
    {
        File.WriteAllText(Path.Combine(_home, "config.json"), """{ "actions": { "destructive": "explode" } }""");

        var (exit, _, _) = RunHook(Payload("git reset --hard"));

        Assert.Equal(0, exit);
        Assert.Contains("unknown action \"explode\"", File.ReadAllText(Path.Combine(_home, "hook-errors.log")));
    }

    [Fact]
    public void HookHonoursConfiguredActionsAndHostTags()
    {
        File.WriteAllText(
            Path.Combine(_home, "config.json"),
            """{ "actions": { "egress": "block" }, "hostTags": { "vps": "prod" } }""");

        var (exit, _, stderr) = RunHook(Payload("ssh vps \"npm install\""));

        Assert.Equal(2, exit);
        Assert.StartsWith("getcmd blocked (egress, project-write): ", stderr);
        Assert.Contains("on prod host vps", stderr);
    }

    [Fact]
    public void FirstUseCreatesConfigWithDefaults()
    {
        Run(null, "check", "ls");

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_home, "config.json")))!;
        Assert.Equal("block", (string?)config["actions"]!["destructive"]);
        Assert.Equal("ask", (string?)config["actions"]!["egress"]);
        Assert.Equal(8, config["buildDirs"]!.AsArray().Count);
        Assert.Empty(config["hostTags"]!.AsObject());
        Assert.Empty(config["disabledRules"]!.AsArray());
        Assert.Equal(30, (int?)config["logRetentionDays"]);
    }

    [Theory]
    [InlineData("ls -la", 0)]
    [InlineData("npm publish", 1)]
    [InlineData("git reset --hard", 2)]
    public void CheckExitCodeFollowsAction(string command, int expected)
    {
        Assert.Equal(expected, Run(null, "check", command, "--cwd", Cwd).Exit);
    }

    [Fact]
    public void CheckPrintsHeaderAndParts()
    {
        var (_, stdout, _) = Run(null, "check", "rm -rf ./build && git push --force", "--cwd", Cwd);

        var lines = stdout.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("DESTRUCTIVE  block   git-force-push", lines[0]);
        Assert.Equal("  rm -rf ./build    mutate       rm-build-dir", lines[1]);
        Assert.Equal("  git push --force  destructive  Force push can overwrite remote history; use --force-with-lease", lines[2]);
    }

    [Fact]
    public void CheckJsonPrintsTheDecision()
    {
        var (exit, stdout, _) = Run(null, "check", "sudo rm -rf ./build", "--cwd", Cwd, "--json");

        var decision = JsonNode.Parse(stdout)!;
        Assert.Equal(1, exit);
        Assert.Equal("egress", (string?)decision["level"]);
        Assert.Equal("ask", (string?)decision["action"]);
        Assert.Equal("rm-build-dir", (string?)decision["ruleId"]);
        Assert.True((bool)decision["sudo"]!);
        Assert.Null(decision["remoteHost"]);
        Assert.Equal("rm -rf ./build", (string?)decision["parts"]![0]!["raw"]);
    }

    [Fact]
    public void LogShowsRecentRowsAndFilters()
    {
        RunHook(Payload("ls -la"));
        RunHook(Payload("git reset --hard"));
        RunHook(Payload("npm publish"));

        var all = Run(null, "log").Out.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(4, all.Length);
        Assert.StartsWith("TIME", all[0]);
        Assert.EndsWith("ls -la", all[1]);
        Assert.EndsWith("npm publish", all[3]);

        var last = Run(null, "log", "--last", "1").Out.TrimEnd().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal(2, last.Length);
        Assert.EndsWith("npm publish", last[1]);

        var blocked = Run(null, "log", "--action", "block").Out;
        Assert.Contains("git reset --hard", blocked);
        Assert.DoesNotContain("npm publish", blocked);

        var destructive = JsonNode.Parse(Run(null, "log", "--level", "destructive", "--json").Out)!.AsArray();
        Assert.Equal("git reset --hard", (string?)Assert.Single(destructive)!["command"]);
    }

    [Fact]
    public void LogRejectsUnknownLevel()
    {
        Assert.NotEqual(0, Run(null, "log", "--level", "catastrophic").Exit);
    }

    [Fact]
    public void RulesListShowsEveryRuleOnce()
    {
        var lines = Run(null, "rules", "list").Out.TrimEnd().ReplaceLineEndings("\n").Split('\n');

        Assert.Equal(29, lines.Length);
        Assert.StartsWith("rm-root-or-home", lines[0]);
        Assert.Contains("destructive", lines[0]);
        Assert.StartsWith("project-write", lines[^1]);
        Assert.Equal(Run(null, "rules").Out, Run(null, "rules", "list").Out);
    }

    [Fact]
    public void DisabledRuleIsSkippedUntilEnabledAgain()
    {
        Assert.Equal(0, Run(null, "rules", "disable", "git-force-push").Exit);
        Assert.Contains("git-force-push", Run(null, "rules", "list").Out.Split('\n').Single(l => l.Contains("(disabled)")));
        Assert.Equal(0, RunHook(Payload("git push --force")).Exit);

        Assert.Equal(0, Run(null, "rules", "enable", "git-force-push").Exit);
        Assert.DoesNotContain("(disabled)", Run(null, "rules", "list").Out);
        Assert.Equal(2, RunHook(Payload("git push --force")).Exit);
    }

    [Fact]
    public void DisablingUnknownRuleFails()
    {
        var (exit, _, stderr) = Run(null, "rules", "disable", "no-such-rule");

        Assert.Equal(1, exit);
        Assert.Contains("unknown rule", stderr);
    }

    [Fact]
    public void RulesTestPasses()
    {
        var (exit, stdout, _) = Run(null, "rules", "test");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("FAIL", stdout);
        Assert.Contains(" passed, 0 failed", stdout);
    }

    [Fact]
    public void EmbeddedCasesMatchTheRepoFile()
    {
        using var reader = new StreamReader(RulesCommand.OpenEmbeddedCases());
        var linked = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules", "cases.json"));

        Assert.Equal(linked, reader.ReadToEnd());
        Assert.True(JsonDocument.Parse(linked).RootElement.GetArrayLength() >= 60);
    }

    [Fact]
    public void DoctorReportsEachCheck()
    {
        var (exit, stdout, _) = Run(null, "doctor");

        // PATH and the Claude hook depend on the machine; the rest must pass here.
        Assert.InRange(exit, 0, 1);
        Assert.Matches(@"OK\s+home writable", stdout);
        Assert.Matches(@"OK\s+log\.db opens", stdout);
        Assert.Matches(@"OK\s+config parses", stdout);
        Assert.Matches(@"OK\s+version\s+0\.0\.1-dev", stdout);
        Assert.Matches(@"(OK|FAIL)\s+binary on PATH", stdout);
        Assert.Matches(@"(OK|FAIL)\s+hook installed", stdout);
    }

    [Fact]
    public void VersionFlags()
    {
        Assert.Equal("getcmd 0.0.1-dev", Run(null, "--version").Out.TrimEnd());
        Assert.Equal("getcmd 0.0.1-dev", Run(null, "-v").Out.TrimEnd());
    }
}
