using System.Text.Json.Nodes;
using Getcmd.Cli.Commands;
using Getcmd.Cli.Services;

namespace Getcmd.Cli.Tests;

public sealed class ClaudeSettingsTests : IDisposable
{
    private const string Existing = """
        {
          "model": "opus",
          "theme": "dark",
          "permissions": { "allow": ["Bash(npm test:*)"], "deny": [] },
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "Bash",
                "hooks": [ { "type": "command", "command": "sh ~/.claude/hooks/other.sh" } ]
              }
            ],
            "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ]
          }
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("getcmd-settings-").FullName;

    private string SettingsPath => Path.Combine(_directory, ".claude", "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void WriteExisting(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);
    }

    private JsonNode Read() => JsonNode.Parse(File.ReadAllText(SettingsPath))!;

    private static void AssertOurEntry(JsonNode? entry, string matcher)
    {
        Assert.Equal(matcher, (string?)entry!["matcher"]);
        var hook = Assert.Single(entry["hooks"]!.AsArray())!;
        Assert.Equal("command", (string?)hook["type"]);
        Assert.Equal("getcmd hook claude", (string?)hook["command"]);
        Assert.Equal(5, (int?)hook["timeout"]);
    }

    [Fact]
    public void InstallMergesIntoExistingSettings()
    {
        WriteExisting(Existing);
        var stdout = new StringWriter();

        Assert.Equal(0, HookCommand.Install(SettingsPath, stdout, windows: false));

        var settings = Read();
        Assert.Equal("opus", (string?)settings["model"]);
        Assert.Equal("dark", (string?)settings["theme"]);
        Assert.Equal("Bash(npm test:*)", (string?)settings["permissions"]!["allow"]![0]);
        Assert.Equal("say done", (string?)settings["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]);

        var entries = settings["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, entries.Count);
        Assert.Equal("sh ~/.claude/hooks/other.sh", (string?)entries[0]!["hooks"]![0]!["command"]);
        AssertOurEntry(entries[1], "Bash");

        Assert.StartsWith("Added PreToolUse hook", stdout.ToString());
        Assert.Contains("matcher \"Bash\"", stdout.ToString());
        Assert.DoesNotContain("PowerShell", stdout.ToString());
        Assert.True(ClaudeSettings.IsInstalled(SettingsPath, "Bash"));
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath, "PowerShell"));
    }

    [Fact]
    public void InstallOnWindowsAddsPowerShellEntryToo()
    {
        WriteExisting(Existing);
        var stdout = new StringWriter();

        HookCommand.Install(SettingsPath, stdout, windows: true);

        var entries = Read()["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(3, entries.Count);
        AssertOurEntry(entries[1], "Bash");
        AssertOurEntry(entries[2], "PowerShell");
        Assert.Contains("matcher \"Bash\"", stdout.ToString());
        Assert.Contains("matcher \"PowerShell\"", stdout.ToString());
        Assert.True(ClaudeSettings.IsInstalled(SettingsPath, "PowerShell"));
    }

    [Fact]
    public void InstallOnWindowsAddsOnlyTheMissingEntry()
    {
        WriteExisting(Existing);
        HookCommand.Install(SettingsPath, new StringWriter(), windows: false);
        var stdout = new StringWriter();

        HookCommand.Install(SettingsPath, stdout, windows: true);

        Assert.Equal(3, Read()["hooks"]!["PreToolUse"]!.AsArray().Count);
        Assert.DoesNotContain("matcher \"Bash\"", stdout.ToString());
        Assert.Contains("matcher \"PowerShell\"", stdout.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstallIsIdempotent(bool windows)
    {
        WriteExisting(Existing);
        HookCommand.Install(SettingsPath, new StringWriter(), windows);
        var afterFirst = File.ReadAllText(SettingsPath);
        var stdout = new StringWriter();

        HookCommand.Install(SettingsPath, stdout, windows);

        Assert.Equal(afterFirst, File.ReadAllText(SettingsPath));
        Assert.Contains("nothing changed", stdout.ToString());
    }

    [Fact]
    public void InstallCreatesSettingsWhenMissing()
    {
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath, "Bash"));

        Assert.Equal(["Bash"], ClaudeSettings.Install(SettingsPath, ClaudeSettings.MatchersFor(windows: false)));

        AssertOurEntry(Assert.Single(Read()["hooks"]!["PreToolUse"]!.AsArray()), "Bash");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UninstallRemovesExactlyOurEntries(bool windows)
    {
        WriteExisting(Existing);
        var before = Read().ToJsonString();
        HookCommand.Install(SettingsPath, new StringWriter(), windows);
        var stdout = new StringWriter();

        Assert.Equal(0, HookCommand.Uninstall(SettingsPath, stdout));

        Assert.Equal(before, Read().ToJsonString());
        Assert.Contains("matcher \"Bash\"", stdout.ToString());
        Assert.Equal(windows, stdout.ToString().Contains("matcher \"PowerShell\""));
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath, "Bash"));
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath, "PowerShell"));
    }

    [Fact]
    public void UninstallDropsContainersItEmptied()
    {
        WriteExisting("""{ "model": "opus" }""");
        ClaudeSettings.Install(SettingsPath, ClaudeSettings.MatchersFor(windows: true));

        Assert.Equal(["Bash", "PowerShell"], ClaudeSettings.Uninstall(SettingsPath));

        var settings = Read().AsObject();
        Assert.Equal("opus", (string?)settings["model"]);
        Assert.False(settings.ContainsKey("hooks"));
    }

    [Fact]
    public void UninstallWithoutEntryChangesNothing()
    {
        WriteExisting(Existing);
        var stdout = new StringWriter();

        HookCommand.Uninstall(SettingsPath, stdout);

        Assert.Equal(Existing, File.ReadAllText(SettingsPath));
        Assert.Contains("nothing changed", stdout.ToString());
    }

    [Fact]
    public void UnexpectedShapeIsLeftUntouched()
    {
        const string odd = """{ "hooks": ["not", "an", "object"] }""";
        WriteExisting(odd);

        Assert.Throws<InvalidDataException>(
            () => ClaudeSettings.Install(SettingsPath, ClaudeSettings.MatchersFor(windows: true)));

        Assert.Equal(odd, File.ReadAllText(SettingsPath));
    }
}
