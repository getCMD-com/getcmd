using System.Text.RegularExpressions;

namespace Getcmd.Core;

public static class RuleEngine
{
    private const string NoRuleReason = "No rule matched; treated as read-only";
    private const string ProdTag = "prod";

    public static Decision Evaluate(string line, Context ctx, IReadOnlyList<Rule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(ctx);
        rules ??= BuiltinRules.All;

        // Flatten to the commands that actually run, remembering where each came from.
        var inner = new List<(SimpleCommand Command, UnwrapResult Source, bool Unwrapped)>();
        var sudo = false;
        string? remoteHost = null;
        foreach (var command in Tokenizer.Split(line))
        {
            var result = Unwrapper.Unwrap(command);
            sudo |= result.Sudo;
            remoteHost ??= result.RemoteHost;
            foreach (var innerCommand in result.Commands)
            {
                inner.Add((innerCommand, result, !ReferenceEquals(innerCommand, command)));
            }
        }

        var parts = new List<PartDecision>(inner.Count);
        PartDecision? top = null;
        for (var i = 0; i < inner.Count; i++)
        {
            var next = i + 1 < inner.Count ? inner[i + 1].Command : null;
            var part = EvaluatePart(inner[i].Command, next, inner[i].Source, inner[i].Unwrapped, ctx, rules);
            parts.Add(part);
            if (top is null || part.Level > top.Level)
            {
                top = part;
            }
        }

        var level = top?.Level ?? Level.Read;
        return new Decision(
            level,
            ctx.Actions.TryGetValue(level, out var action) ? action : Action.Ask,
            top?.RuleId,
            top?.Reason ?? NoRuleReason,
            parts,
            sudo,
            remoteHost);
    }

    private static PartDecision EvaluatePart(
        SimpleCommand command,
        SimpleCommand? next,
        UnwrapResult source,
        bool unwrapped,
        Context ctx,
        IReadOnlyList<Rule> rules)
    {
        var prodHost = source.RemoteHost is { } host
            && ctx.HostTags.TryGetValue(host, out var tag)
            && string.Equals(tag, ProdTag, StringComparison.OrdinalIgnoreCase)
                ? host
                : null;

        Rule? rule = null;
        var program = Unwrapper.ProgramName(command.Program);
        foreach (var candidate in rules)
        {
            if (candidate.Id != BuiltinRules.SshRemoteCommandId && Matches(candidate, command, program, next, ctx))
            {
                rule = candidate;
                break;
            }
        }

        // A remote command nothing else classified is silent, except on prod.
        if (rule is null && prodHost is not null && unwrapped)
        {
            rule = FindRemoteRule(rules);
        }

        var level = rule?.Level ?? Level.Read;
        var reason = rule?.Reason ?? NoRuleReason;

        if (source.Sudo)
        {
            level = level switch
            {
                Level.Read => Level.Mutate,
                Level.Mutate => Level.Egress,
                _ => Level.Destructive,
            };
            reason += "; runs as root";
        }

        if (prodHost is not null)
        {
            if (level == Level.Mutate)
            {
                level = Level.Egress;
            }

            reason += $"; on prod host {prodHost}";
        }

        if (source.DepthExceeded)
        {
            if (level < Level.Egress)
            {
                level = Level.Egress;
            }

            reason += "; nested wrappers too deep to inspect";
        }

        return new PartDecision(command.Raw, level, rule?.Id, reason);
    }

    private static Rule? FindRemoteRule(IReadOnlyList<Rule> rules)
    {
        foreach (var rule in rules)
        {
            if (rule.Id == BuiltinRules.SshRemoteCommandId)
            {
                return rule;
            }
        }

        return null;
    }

    private static bool Matches(Rule rule, SimpleCommand command, string program, SimpleCommand? next, Context ctx)
    {
        if (rule.Programs.Count > 0 && !MatchesAnyName(rule.Programs, program))
        {
            return false;
        }

        foreach (var pattern in rule.AllArgs)
        {
            if (!HasArg(command.Args, pattern))
            {
                return false;
            }
        }

        if (rule.AnyArgs.Count > 0 && !HasAnyArg(command.Args, rule.AnyArgs))
        {
            return false;
        }

        if (HasAnyArg(command.Args, rule.NoneArgs))
        {
            return false;
        }

        if (rule.PathScopes.Count > 0 && !HasPathInScopes(command.Args, rule.PathScopes, ctx))
        {
            return false;
        }

        if (rule.RawPattern is { } rawPattern && !rawPattern.IsMatch(command.Raw))
        {
            return false;
        }

        if (rule.RedirectPatterns.Count > 0 && !WritesToMatchingTarget(command.Redirections, rule.RedirectPatterns))
        {
            return false;
        }

        if (rule.PipedTo is { } pipedTo)
        {
            if (next is null || next.Before != Connector.Pipe)
            {
                return false;
            }

            if (!MatchesAnyName(pipedTo.Split('|'), Unwrapper.ProgramName(next.Program)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAnyName(IReadOnlyList<string> patterns, string name)
    {
        foreach (var pattern in patterns)
        {
            var matches = pattern.Length > 1 && pattern[^1] == '*'
                ? name.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal)
                : name == pattern;
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyArg(IReadOnlyList<string> args, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (HasArg(args, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasArg(IReadOnlyList<string> args, string pattern)
    {
        foreach (var arg in args)
        {
            if (ArgMatches(arg, pattern))
            {
                return true;
            }
        }

        return false;
    }

    // See the pattern forms documented on Rule.
    private static bool ArgMatches(string arg, string pattern)
    {
        if (pattern.Length == 3 && pattern[0] == '-' && pattern[1] != '-' && pattern[2] == '*')
        {
            return arg.Length >= 2 && arg[0] == '-' && arg[1] != '-' && arg.Contains(pattern[1]);
        }

        if (pattern.Length > 1 && pattern[^1] == '*')
        {
            return arg.StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal);
        }

        if (pattern.Length > 2 && pattern.StartsWith("--", StringComparison.Ordinal))
        {
            return arg.StartsWith(pattern, StringComparison.Ordinal);
        }

        return arg == pattern;
    }

    private static bool HasPathInScopes(IReadOnlyList<string> args, IReadOnlyList<PathScope> scopes, Context ctx)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
            {
                continue;
            }

            var scope = PathResolver.Classify(arg, ctx.Cwd, ctx.Home, ctx.BuildDirs);
            foreach (var wanted in scopes)
            {
                if (scope == wanted)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool WritesToMatchingTarget(IReadOnlyList<Redirection> redirections, IReadOnlyList<Regex> patterns)
    {
        foreach (var redirection in redirections)
        {
            if (!redirection.Operator.Contains('>'))
            {
                continue;
            }

            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(redirection.Target))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
