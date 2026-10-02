using System.Text;

namespace Getcmd.Core;

public sealed record UnwrapResult(
    IReadOnlyList<SimpleCommand> Commands,
    bool Sudo,
    string? RemoteHost,
    string? Wrapper);

/// <summary>
/// Peels wrappers (sudo, env, bash -c, xargs, ssh, ...) off a command to expose
/// the commands that actually run.
/// </summary>
public static class Unwrapper
{
    private const int MaxDepth = 4;
    private const string XargsInput = "{xargs-input}";

    // Options that take a separate value, per wrapper.
    private const string SudoShortWithValue = "ugpCDrtTUhR";
    private const string SshShortWithValue = "BbcDEeFIiJLlmOopQRSWw";
    private const string XargsShortWithValue = "adEILnPs";
    private static readonly string[] NoLongOptions = [];
    private static readonly string[] SudoLongWithValue =
    [
        "--user", "--group", "--host", "--prompt", "--chdir", "--chroot", "--role", "--type",
        "--other-user", "--close-from", "--command-timeout",
    ];
    private static readonly string[] EnvLongWithValue = ["--unset", "--chdir"];
    private static readonly string[] NiceLongWithValue = ["--adjustment"];
    private static readonly string[] TimeoutLongWithValue = ["--signal", "--kill-after"];
    private static readonly string[] XargsLongWithValue =
        ["--arg-file", "--delimiter", "--max-args", "--max-procs", "--max-chars"];

    private sealed class State
    {
        public bool Sudo;
        public string? RemoteHost;
        public string? Wrapper;
    }

    public static UnwrapResult Unwrap(SimpleCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var state = new State();
        var commands = new List<SimpleCommand>();
        Expand(cmd, 0, state, commands);
        return new UnwrapResult(commands, state.Sudo, state.RemoteHost, state.Wrapper);
    }

    private static void Expand(SimpleCommand cmd, int depth, State state, List<SimpleCommand> output)
    {
        if (depth >= MaxDepth || !TryUnwrap(cmd, depth, state, output))
        {
            output.Add(cmd);
        }
    }

    // Applies one rule to cmd and expands the result. Returns false, adding
    // nothing to output, when no rule matches.
    private static bool TryUnwrap(SimpleCommand cmd, int depth, State state, List<SimpleCommand> output)
    {
        var args = cmd.Args;
        var name = ProgramName(cmd.Program);

        switch (name)
        {
            case "bash" or "sh" or "zsh" or "dash":
                return ShellCommandString(args) is { } script
                    && ExpandString(script, "bash -c", cmd, depth, state, output);

            case "eval":
                return ExpandString(string.Join(' ', args), "eval", cmd, depth, state, output);

            case "ssh":
            {
                var host = SkipOptions(args, 0, SshShortWithValue, NoLongOptions);
                if (host >= args.Count)
                {
                    return false;
                }

                state.RemoteHost ??= HostOf(args[host]);

                // ssh joins everything after the host into one remote command line.
                return host + 1 < args.Count
                    && ExpandString(string.Join(' ', args.Skip(host + 1)), "ssh", cmd, depth, state, output);
            }

            case "xargs":
            {
                var start = SkipOptions(args, 0, XargsShortWithValue, XargsLongWithValue);
                if (start >= args.Count)
                {
                    return false;
                }

                state.Wrapper ??= "xargs";
                var words = new List<string>(args.Skip(start)) { XargsInput };
                Expand(FromWords(words, cmd.Before, cmd.Redirections), depth + 1, state, output);
                return true;
            }

            case "find":
                return ExpandFindExec(cmd, depth, state, output);

            default:
            {
                var start = InnerCommandStart(name, cmd, state);
                if (start < 0)
                {
                    return false;
                }

                var words = new List<string>(args.Skip(start));
                Expand(FromWords(words, cmd.Before, cmd.Redirections), depth + 1, state, output);
                return true;
            }
        }
    }

    // For prefix wrappers (sudo, env, nohup, ...): the index in cmd.Args where
    // the wrapped command starts, or -1 if cmd is not such a wrapper.
    private static int InnerCommandStart(string name, SimpleCommand cmd, State state)
    {
        var args = cmd.Args;
        int start;

        switch (name)
        {
            case "sudo":
                start = SkipOptions(args, 0, SudoShortWithValue, SudoLongWithValue);
                if (HasShortFlag(args, start, "el"))
                {
                    // sudo -e (edit) and -l (list) do not run the command.
                    return -1;
                }

                start = SkipAssignments(args, start);
                if (start >= args.Count)
                {
                    return -1;
                }

                state.Sudo = true;
                return start;

            case "doas":
                start = SkipOptions(args, 0, "uC", NoLongOptions);
                if (start >= args.Count)
                {
                    return -1;
                }

                state.Sudo = true;
                return start;

            case "env":
                start = SkipAssignments(args, SkipOptions(args, 0, "uC", EnvLongWithValue));
                break;

            case "nohup" or "time":
                start = SkipOptions(args, 0, "", NoLongOptions);
                break;

            case "nice":
                start = SkipOptions(args, 0, "n", NiceLongWithValue);
                break;

            case "timeout":
                // Options, then the duration, then the command.
                start = SkipOptions(args, 0, "sk", TimeoutLongWithValue) + 1;
                break;

            case "command":
                start = SkipOptions(args, 0, "", NoLongOptions);
                if (HasShortFlag(args, start, "vV"))
                {
                    // command -v / -V only looks the name up.
                    return -1;
                }
                break;

            case "exec":
                start = SkipOptions(args, 0, "a", NoLongOptions);
                break;

            default:
                // "VAR=val cmd": the tokeniser reports the assignment as the program.
                if (!IsAssignment(cmd.Program))
                {
                    return -1;
                }

                start = SkipAssignments(args, 0);
                break;
        }

        return start < args.Count ? start : -1;
    }

    private static bool ExpandString(
        string script, string wrapper, SimpleCommand cmd, int depth, State state, List<SimpleCommand> output)
    {
        var inner = Tokenizer.Split(script);
        if (inner.Count == 0)
        {
            return false;
        }

        state.Wrapper ??= wrapper;
        for (var i = 0; i < inner.Count; i++)
        {
            var command = inner[i];
            if (i == 0)
            {
                command = command with { Before = cmd.Before };
            }

            if (i == inner.Count - 1 && cmd.Redirections.Count > 0)
            {
                // Redirections on the wrapper itself would otherwise be lost.
                command = command with { Redirections = [.. command.Redirections, .. cmd.Redirections] };
            }

            Expand(command, depth + 1, state, output);
        }

        return true;
    }

    private static bool ExpandFindExec(SimpleCommand cmd, int depth, State state, List<SimpleCommand> output)
    {
        var args = cmd.Args;
        var found = false;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is not ("-exec" or "-execdir" or "-ok" or "-okdir"))
            {
                continue;
            }

            var end = i + 1;
            while (end < args.Count && args[end] is not (";" or "+"))
            {
                end++;
            }

            if (end > i + 1)
            {
                if (!found)
                {
                    output.Add(cmd);
                    state.Wrapper ??= "find -exec";
                    found = true;
                }

                var words = new List<string>(args.Skip(i + 1).Take(end - i - 1));
                Expand(FromWords(words, null, []), depth + 1, state, output);
            }

            i = end;
        }

        return found;
    }

    // The STRING of "sh -c STRING" (also -lc, -ec, ...), or null when the shell
    // is not given a command string.
    private static string? ShellCommandString(IReadOnlyList<string> args)
    {
        var sawC = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                return sawC && i + 1 < args.Count ? args[i + 1] : null;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg is "--rcfile" or "--init-file")
                {
                    i++;
                }
            }
            else if (arg.Length > 1 && arg[0] is '-' or '+')
            {
                if (arg[0] == '-' && arg.Contains('c'))
                {
                    sawC = true;
                }

                if (arg[^1] is 'o' or 'O')
                {
                    // "-o pipefail" / "-O extglob" take a value.
                    i++;
                }
            }
            else
            {
                return sawC ? arg : null;
            }
        }

        return null;
    }

    // Returns the index of the first argument at or after start that is not an
    // option (or an option's value). A lone "--" is consumed.
    private static int SkipOptions(
        IReadOnlyList<string> args, int start, string shortWithValue, string[] longWithValue)
    {
        var i = start;
        while (i < args.Count)
        {
            var arg = args[i];
            if (arg == "--")
            {
                i++;
                break;
            }

            if (arg.Length < 2 || arg[0] != '-')
            {
                break;
            }

            i++;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (Array.IndexOf(longWithValue, arg) >= 0)
                {
                    i++;
                }

                continue;
            }

            // Short cluster: the first value-taking letter ends it, and its value
            // is the next argument unless attached ("-n5").
            for (var j = 1; j < arg.Length; j++)
            {
                if (shortWithValue.Contains(arg[j]))
                {
                    if (j == arg.Length - 1)
                    {
                        i++;
                    }

                    break;
                }
            }
        }

        return Math.Min(i, args.Count);
    }

    private static bool HasShortFlag(IReadOnlyList<string> args, int end, string flags)
    {
        for (var i = 0; i < end; i++)
        {
            var arg = args[i];
            if (arg.Length > 1 && arg[0] == '-' && arg[1] != '-' && arg.AsSpan(1).IndexOfAny(flags) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int SkipAssignments(IReadOnlyList<string> args, int start)
    {
        var i = start;
        while (i < args.Count && IsAssignment(args[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsAssignment(string word)
    {
        var equals = word.IndexOf('=');
        if (equals <= 0 || char.IsAsciiDigit(word[0]))
        {
            return false;
        }

        for (var i = 0; i < equals; i++)
        {
            if (!char.IsAsciiLetterOrDigit(word[i]) && word[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string ProgramName(string program)
    {
        var name = program[(program.AsSpan().LastIndexOfAny('/', '\\') + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static string HostOf(string target) => target[(target.LastIndexOf('@') + 1)..];

    // Builds a command from already-tokenised words. There is no original text
    // for it, so Raw is the words re-quoted.
    private static SimpleCommand FromWords(
        List<string> words, Connector? before, IReadOnlyList<Redirection> redirections)
    {
        var raw = new StringBuilder();
        foreach (var word in words)
        {
            if (raw.Length > 0)
            {
                raw.Append(' ');
            }

            if (word.Length > 0 && word.AsSpan().IndexOfAny(" \t\r\n'\"\\;&|<>()#`") < 0)
            {
                raw.Append(word);
            }
            else
            {
                raw.Append('\'').Append(word.Replace("'", "'\\''")).Append('\'');
            }
        }

        return new SimpleCommand(words[0], words.GetRange(1, words.Count - 1), raw.ToString(), before, redirections);
    }
}
