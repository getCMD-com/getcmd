using System.Text.RegularExpressions;

namespace Getcmd.Core;

/// <summary>
/// One classification rule. Every condition that is set must hold for the rule
/// to match. Several rules may share an Id to express alternatives.
/// </summary>
/// <remarks>
/// Programs match by name, or by prefix when the entry ends in '*' ("mkfs*").
/// Arg patterns (AllArgs, AnyArgs, NoneArgs):
///   "-r*"      a single-dash flag cluster containing r ("-r", "-rf", "-fr")
///   "--force"  prefix match ("--force", "--force=x", "--force-with-lease")
///   "db:*"     prefix match for anything else ending in '*'
///   otherwise  exact match
/// PathScopes looks at every argument that is not a flag. RawRegex and
/// RedirectTargetsRegex are case-insensitive; redirect targets are only checked
/// for output redirections. PipedTo is a '|'-separated list of program names.
/// </remarks>
public sealed record Rule(
    string Id,
    Level Level,
    string Reason,
    IReadOnlyList<string> Programs,
    IReadOnlyList<string> AllArgs,
    IReadOnlyList<string> AnyArgs,
    IReadOnlyList<string> NoneArgs,
    IReadOnlyList<PathScope> PathScopes,
    string? RawRegex,
    IReadOnlyList<string> RedirectTargetsRegex,
    string? PipedTo)
{
    // Regexes are built once, here, and interpreted (no RegexOptions.Compiled),
    // which keeps user-supplied patterns usable under Native AOT.
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly Regex? _rawPattern = Compile(RawRegex);
    private readonly Regex[] _redirectPatterns = CompileAll(RedirectTargetsRegex);

    public string? RawRegex
    {
        get;
        init
        {
            field = value;
            _rawPattern = Compile(value);
        }
    } = RawRegex;

    public IReadOnlyList<string> RedirectTargetsRegex
    {
        get;
        init
        {
            field = value;
            _redirectPatterns = CompileAll(value);
        }
    } = RedirectTargetsRegex;

    internal Regex? RawPattern => _rawPattern;

    internal IReadOnlyList<Regex> RedirectPatterns => _redirectPatterns;

    private static Regex? Compile(string? pattern) => pattern is null ? null : new Regex(pattern, Options);

    private static Regex[] CompileAll(IReadOnlyList<string> patterns)
    {
        var compiled = new Regex[patterns.Count];
        for (var i = 0; i < compiled.Length; i++)
        {
            compiled[i] = new Regex(patterns[i], Options);
        }

        return compiled;
    }
}
