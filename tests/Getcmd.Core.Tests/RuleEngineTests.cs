using System.Text.Json;

namespace Getcmd.Core.Tests;

public class RuleEngineTests
{
    private const string DefaultCwd = "/home/ally/src/app";
    private const string Home = "/home/ally";

    private static readonly string[] BuildDirs = ["build", "dist", "node_modules"];

    private static readonly Dictionary<Level, Action> Actions = new()
    {
        [Level.Read] = Action.Allow,
        [Level.Mutate] = Action.Allow,
        [Level.Egress] = Action.Ask,
        [Level.Secrets] = Action.Ask,
        [Level.Destructive] = Action.Block,
    };

    private static Context MakeContext(string? cwd = null, Dictionary<string, string>? hostTags = null) =>
        new(cwd ?? DefaultCwd, Home, BuildDirs, hostTags ?? [], Actions);

    private static JsonElement LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    // command, level, rule, cwd, hostTags as "host=tag;host=tag"
    public static TheoryData<string, string, string?, string?, string?> Cases()
    {
        var data = new TheoryData<string, string, string?, string?, string?>();
        foreach (var item in LoadCases().EnumerateArray())
        {
            var cwd = item.TryGetProperty("cwd", out var cwdElement) ? cwdElement.GetString() : null;
            var hostTags = item.TryGetProperty("hostTags", out var tagsElement)
                ? string.Join(';', tagsElement.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetString()}"))
                : null;

            data.Add(
                item.GetProperty("command").GetString()!,
                item.GetProperty("level").GetString()!,
                item.GetProperty("rule").GetString(),
                cwd,
                hostTags);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CasesFile(string command, string level, string? rule, string? cwd, string? hostTags)
    {
        var tags = (hostTags ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        var decision = RuleEngine.Evaluate(command, MakeContext(cwd, tags));

        Assert.Equal(Enum.Parse<Level>(level), decision.Level);
        Assert.Equal(rule, decision.RuleId);
    }

    [Fact]
    public void CasesFileCoversEveryBuiltinRule()
    {
        var covered = LoadCases().EnumerateArray()
            .Select(item => item.GetProperty("rule").GetString())
            .ToHashSet();

        Assert.True(LoadCases().GetArrayLength() >= 60);
        Assert.All(BuiltinRules.All, rule => Assert.Contains(rule.Id, covered));
    }

    [Fact]
    public void BuiltinRuleIdsAreInTheDocumentedOrder()
    {
        string[] expected =
        [
            "rm-root-or-home", "rm-recursive", "rm-build-dir", "rm-file", "git-force-push", "git-reset-hard",
            "git-clean", "git-checkout-discard", "git-branch-delete", "git-write", "sql-drop", "sql-delete-all",
            "db-drop", "docker-destroy", "kubectl-delete", "disk-write", "perm-bomb", "kill-all", "curl-pipe-sh",
            "curl-upload", "remote-copy", "ssh-remote-cmd", "publish", "read-credentials", "print-secret",
            "creds-in-command", "write-sensitive", "build-tool", "project-write",
        ];

        // Alternatives of one rule share an id and sit next to each other.
        var ids = new List<string>();
        foreach (var rule in BuiltinRules.All)
        {
            if (ids.Count == 0 || ids[^1] != rule.Id)
            {
                ids.Add(rule.Id);
            }
        }

        Assert.Equal(expected, ids);
    }

    [Theory]
    [InlineData("ls -la", Action.Allow)]
    [InlineData("npm install", Action.Allow)]
    [InlineData("npm publish", Action.Ask)]
    [InlineData("cat .env", Action.Ask)]
    [InlineData("git reset --hard", Action.Block)]
    public void ActionComesFromContext(string command, Action expected)
    {
        Assert.Equal(expected, RuleEngine.Evaluate(command, MakeContext()).Action);
    }

    [Fact]
    public void MissingActionDefaultsToAsk()
    {
        var ctx = MakeContext() with { Actions = new Dictionary<Level, Action>() };

        Assert.Equal(Action.Ask, RuleEngine.Evaluate("ls", ctx).Action);
    }

    [Fact]
    public void PartsAreReportedPerInnerCommand()
    {
        var decision = RuleEngine.Evaluate("rm -rf ./build && git push --force", MakeContext());

        Assert.Equal(2, decision.Parts.Count);
        Assert.Equal(new PartDecision("rm -rf ./build", Level.Mutate, "rm-build-dir", "Recursive delete of build output"), decision.Parts[0]);
        Assert.Equal("git push --force", decision.Parts[1].Raw);
        Assert.Equal(Level.Destructive, decision.Parts[1].Level);
        Assert.Equal("git-force-push", decision.Parts[1].RuleId);
        Assert.Equal(decision.Parts[1].Reason, decision.Reason);
        Assert.False(decision.Sudo);
        Assert.Null(decision.RemoteHost);
    }

    [Fact]
    public void FirstPartAtTheTopLevelProvidesRuleAndReason()
    {
        var decision = RuleEngine.Evaluate("git reset --hard; git clean -fd", MakeContext());

        Assert.Equal(Level.Destructive, decision.Level);
        Assert.Equal("git-reset-hard", decision.RuleId);
    }

    [Fact]
    public void UnmatchedCommandIsReadOnly()
    {
        var decision = RuleEngine.Evaluate("ls -la", MakeContext());

        Assert.Equal(Level.Read, decision.Level);
        Assert.Null(decision.RuleId);
        Assert.Equal("No rule matched; treated as read-only", decision.Reason);
        Assert.Equal("ls -la", Assert.Single(decision.Parts).Raw);
    }

    [Fact]
    public void EmptyLineIsReadOnlyWithNoParts()
    {
        var decision = RuleEngine.Evaluate("  # just a comment", MakeContext());

        Assert.Equal(Level.Read, decision.Level);
        Assert.Equal(Action.Allow, decision.Action);
        Assert.Empty(decision.Parts);
    }

    [Fact]
    public void SudoRaisesOneLevelAndIsReported()
    {
        var decision = RuleEngine.Evaluate("sudo apt install jq", MakeContext());

        Assert.Equal(Level.Mutate, decision.Level);
        Assert.True(decision.Sudo);
        Assert.Equal("No rule matched; treated as read-only; runs as root", decision.Reason);
        Assert.Equal("apt install jq", Assert.Single(decision.Parts).Raw);
    }

    [Fact]
    public void SudoOnlyAffectsItsOwnCommand()
    {
        var decision = RuleEngine.Evaluate("ls && sudo ls", MakeContext());

        Assert.Equal(Level.Read, decision.Parts[0].Level);
        Assert.Equal(Level.Mutate, decision.Parts[1].Level);
        Assert.True(decision.Sudo);
    }

    [Fact]
    public void ProdHostEscalatesAndIsNamedInReason()
    {
        var ctx = MakeContext(hostTags: new() { ["vps"] = "prod" });

        var decision = RuleEngine.Evaluate("ssh vps \"npm install\"", ctx);

        Assert.Equal(Level.Egress, decision.Level);
        Assert.Equal("vps", decision.RemoteHost);
        Assert.EndsWith("; on prod host vps", decision.Reason);
    }

    [Fact]
    public void ProdHostKeepsDestructiveAndMatchedRead()
    {
        var ctx = MakeContext(hostTags: new() { ["vps"] = "prod" });

        Assert.Equal(Level.Destructive, RuleEngine.Evaluate("ssh vps 'git reset --hard'", ctx).Level);

        var build = RuleEngine.Evaluate("ssh vps make", ctx);
        Assert.Equal(Level.Read, build.Level);
        Assert.Equal("build-tool", build.RuleId);
    }

    [Fact]
    public void UntaggedHostDoesNotChangeMutate()
    {
        var decision = RuleEngine.Evaluate("ssh vps \"npm install\"", MakeContext());

        Assert.Equal(Level.Mutate, decision.Level);
        Assert.Equal("project-write", decision.RuleId);
        Assert.Equal("vps", decision.RemoteHost);
    }

    [Fact]
    public void DepthExceededIsAtLeastEgress()
    {
        var decision = RuleEngine.Evaluate("env A=1 env B=2 env C=3 env D=4 env E=5 ls", MakeContext());

        Assert.Equal(Level.Egress, decision.Level);
        Assert.Null(decision.RuleId);
        Assert.EndsWith("; nested wrappers too deep to inspect", decision.Reason);
    }

    [Fact]
    public void CustomRulesReplaceTheBuiltins()
    {
        Rule[] rules =
        [
            new("no-ls", Level.Secrets, "ls is off limits", ["ls"], [], [], [], [], null, [], null),
        ];

        var decision = RuleEngine.Evaluate("ls && git reset --hard", MakeContext(), rules);

        Assert.Equal(Level.Secrets, decision.Level);
        Assert.Equal("no-ls", decision.RuleId);
        Assert.Null(decision.Parts[1].RuleId);
    }

    [Fact]
    public void RuleCopiedWithNewRegexUsesTheNewRegex()
    {
        var rule = new Rule("r", Level.Mutate, "reason", [], [], [], [], [], "alpha", [], null);
        Rule[] rules = [rule with { RawRegex = "beta", RedirectTargetsRegex = ["gamma"] }];

        Assert.Null(RuleEngine.Evaluate("echo alpha > gamma", MakeContext(), rules).RuleId);
        Assert.Null(RuleEngine.Evaluate("echo beta > delta", MakeContext(), rules).RuleId);
        Assert.Equal("r", RuleEngine.Evaluate("echo BETA > gamma", MakeContext(), rules).RuleId);
    }

    [Fact]
    public void InputRedirectionIsNotAWrite()
    {
        var decision = RuleEngine.Evaluate("sort < ~/.ssh/known_hosts", MakeContext());

        Assert.Equal(Level.Read, decision.Level);
    }
}
