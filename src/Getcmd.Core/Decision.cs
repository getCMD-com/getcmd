namespace Getcmd.Core;

public sealed record PartDecision(string Raw, Level Level, string? RuleId, string Reason);

public sealed record Decision(
    Level Level,
    Action Action,
    string? RuleId,
    string Reason,
    IReadOnlyList<PartDecision> Parts,
    bool Sudo,
    string? RemoteHost);
