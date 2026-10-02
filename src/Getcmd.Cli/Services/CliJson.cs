using System.Text.Json;
using System.Text.Json.Serialization;
using Getcmd.Core;

namespace Getcmd.Cli.Services;

// All JSON goes through these source-generated contexts; nothing here may use
// reflection-based serialization.

/// <summary>The PreToolUse payload Claude Code writes to the hook's stdin.</summary>
internal sealed class HookInput
{
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("tool_input")]
    public HookToolInput? ToolInput { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }
}

internal sealed class HookToolInput
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed record HookOutput(HookSpecificOutput HookSpecificOutput);

internal sealed record HookSpecificOutput(
    string HookEventName,
    string PermissionDecision,
    string PermissionDecisionReason);

internal sealed record DecisionJson(
    string Level,
    string Action,
    string? RuleId,
    string Reason,
    IReadOnlyList<PartJson> Parts,
    bool Sudo,
    string? RemoteHost)
{
    public static DecisionJson From(Decision decision)
    {
        var parts = new PartJson[decision.Parts.Count];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = decision.Parts[i];
            parts[i] = new PartJson(part.Raw, Names.Of(part.Level), part.RuleId, part.Reason);
        }

        return new DecisionJson(
            Names.Of(decision.Level),
            Names.Of(decision.Action),
            decision.RuleId,
            decision.Reason,
            parts,
            decision.Sudo,
            decision.RemoteHost);
    }
}

internal sealed record PartJson(string Raw, string Level, string? RuleId, string Reason);

/// <summary>One row of the decisions table.</summary>
internal sealed record LogEntry(
    long Id,
    string Ts,
    string Agent,
    string? SessionId,
    string Cwd,
    string Command,
    string Level,
    string Action,
    string? RuleId,
    string Reason,
    long DurationMs);

/// <summary>One entry of rules/cases.json.</summary>
internal sealed class RuleCase
{
    public string Command { get; set; } = "";

    public string Level { get; set; } = "";

    public string? Rule { get; set; }

    public string? Cwd { get; set; }

    public Dictionary<string, string>? HostTags { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(DecisionJson))]
[JsonSerializable(typeof(List<LogEntry>))]
[JsonSerializable(typeof(List<RuleCase>))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

// Single-line output for the hook protocol.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HookInput))]
[JsonSerializable(typeof(HookOutput))]
internal sealed partial class HookJsonContext : JsonSerializerContext;
