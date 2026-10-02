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

    [Fact]
    public void InstallMergesIntoExistingSettings()
    {
        WriteExisting(Existing);
        var stdout = new StringWriter();

        Assert.Equal(0, HookCommand.Install(SettingsPath, stdout));

        var settings = Read();
        Assert.Equal("opus", (string?)settings["model"]);
        Assert.Equal("dark", (string?)settings["theme"]);
        Assert.Equal("Bash(npm test:*)", (string?)settings["permissions"]!["allow"]![0]);
        Assert.Equal("say done", (string?)settings["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]);

        var entries = settings["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, entries.Count);
        Assert.Equal("sh ~/.claude/hooks/other.sh", (string?)entries[0]!["hooks"]![0]!["command"]);
        Assert.Equal("Bash", (string?)entries[1]!["matcher"]);

        var hook = Assert.Single(entries[1]!["hooks"]!.AsArray())!;
        Assert.Equal("command", (string?)hook["type"]);
        Assert.Equal("getcmd hook claude", (string?)hook["command"]);
        Assert.Equal(5, (int?)hook["timeout"]);

        Assert.StartsWith("Added PreToolUse hook", stdout.ToString());
        Assert.True(ClaudeSettings.IsInstalled(SettingsPath));
    }

    [Fact]
    public void InstallIsIdempotent()
    {
        WriteExisting(Existing);
        HookCommand.Install(SettingsPath, new StringWriter());
        var afterFirst = File.ReadAllText(SettingsPath);
        var stdout = new StringWriter();

        HookCommand.Install(SettingsPath, stdout);

        Assert.Equal(afterFirst, File.ReadAllText(SettingsPath));
        Assert.Contains("nothing changed", stdout.ToString());
    }

    [Fact]
    public void InstallCreatesSettingsWhenMissing()
    {
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath));

        Assert.True(ClaudeSettings.Install(SettingsPath));

        var entry = Assert.Single(Read()["hooks"]!["PreToolUse"]!.AsArray())!;
        Assert.Equal("getcmd hook claude", (string?)entry["hooks"]![0]!["command"]);
    }

    [Fact]
    public void UninstallRemovesExactlyOurEntry()
    {
        WriteExisting(Existing);
        var before = Read().ToJsonString();
        HookCommand.Install(SettingsPath, new StringWriter());
        var stdout = new StringWriter();

        Assert.Equal(0, HookCommand.Uninstall(SettingsPath, stdout));

        Assert.Equal(before, Read().ToJsonString());
        Assert.StartsWith("Removed PreToolUse hook", stdout.ToString());
        Assert.False(ClaudeSettings.IsInstalled(SettingsPath));
    }

    [Fact]
    public void UninstallDropsContainersItEmptied()
    {
        WriteExisting("""{ "model": "opus" }""");
        ClaudeSettings.Install(SettingsPath);

        Assert.True(ClaudeSettings.Uninstall(SettingsPath));

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

        Assert.Throws<InvalidDataException>(() => ClaudeSettings.Install(SettingsPath));

        Assert.Equal(odd, File.ReadAllText(SettingsPath));
    }
}
