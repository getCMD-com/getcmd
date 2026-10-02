namespace Getcmd.Core;

public sealed record Context(
    string Cwd,
    string Home,
    IReadOnlyList<string> BuildDirs,
    IReadOnlyDictionary<string, string> HostTags,
    IReadOnlyDictionary<Level, Action> Actions);
