using System.Text;

namespace Getcmd.Core;

public enum Connector { Pipe, And, Or, Semicolon, Newline, Background }

public sealed record Redirection(string Operator, string Target);

public sealed record SimpleCommand(
    string Program,
    IReadOnlyList<string> Args,
    string Raw,
    Connector? Before,
    IReadOnlyList<Redirection> Redirections);

/// <summary>
/// POSIX-style tokeniser for a single command line. Splits the line into simple
/// commands on unquoted connectors and each command into words. Nothing is
/// expanded: variables, globs and command substitutions are kept as written.
/// </summary>
public static class Tokenizer
{
    public static IReadOnlyList<SimpleCommand> Split(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var commands = new List<SimpleCommand>();
        var tokens = new List<string>();
        var redirections = new List<Redirection>();
        var token = new StringBuilder();
        var inToken = false;
        // True while the current token holds only unquoted, unescaped characters.
        var plain = true;
        // Operator of a redirection that is still waiting for its target token.
        string? redirect = null;
        // True between a PowerShell call operator and the end of the program token.
        var callTarget = false;
        var commandStart = 0;
        Connector? pending = null;
        var n = line.Length;
        var i = 0;

        void EndToken()
        {
            if (inToken)
            {
                if (redirect is null)
                {
                    tokens.Add(token.ToString());
                }
                else
                {
                    redirections.Add(new Redirection(redirect, token.ToString()));
                    redirect = null;
                }

                token.Clear();
                inToken = false;
                callTarget = false;
            }

            plain = true;
        }

        void BeginRedirect(string op, bool allowFd)
        {
            // Unquoted digits touching the operator are its file descriptor ("2>").
            var fd = "";
            if (allowFd && redirect is null && inToken && plain && IsAllDigits(token))
            {
                fd = token.ToString();
                token.Clear();
                inToken = false;
            }
            else
            {
                EndToken();
            }

            if (redirect is not null)
            {
                redirections.Add(new Redirection(redirect, ""));
            }

            redirect = fd + op;
        }

        void EndCommand(int end, Connector? next)
        {
            EndToken();
            callTarget = false;
            if (redirect is not null)
            {
                redirections.Add(new Redirection(redirect, ""));
                redirect = null;
            }

            if (tokens.Count > 0 || redirections.Count > 0)
            {
                commands.Add(new SimpleCommand(
                    tokens.Count > 0 ? tokens[0] : "",
                    tokens.Count > 0 ? tokens.GetRange(1, tokens.Count - 1) : [],
                    line[commandStart..end].Trim(),
                    commands.Count == 0 ? null : pending,
                    redirections.ToArray()));
                tokens.Clear();
                redirections.Clear();
                pending = next;
            }
            else if (next != Connector.Newline || pending is null)
            {
                // Empty command: the latest connector wins, except that a newline
                // after an operator ("a &&\n b") is just a continuation.
                pending = next;
            }
        }

        while (i < n)
        {
            var c = line[i];
            var next = i + 1 < n ? line[i + 1] : '\0';

            switch (c)
            {
                case '\\' when callTarget:
                    // A bare Windows path after "&": backslashes are separators, not escapes.
                    token.Append('\\');
                    inToken = true;
                    plain = false;
                    i++;
                    break;

                case '\\':
                    if (i + 1 >= n)
                    {
                        token.Append('\\');
                        inToken = true;
                        plain = false;
                        i++;
                    }
                    else if (next == '\n')
                    {
                        // Line continuation.
                        i += 2;
                    }
                    else
                    {
                        token.Append(next);
                        inToken = true;
                        plain = false;
                        i += 2;
                    }
                    break;

                case '\'':
                {
                    var end = SkipSingleQuoted(line, i);
                    var close = end > i + 1 && line[end - 1] == '\'' ? end - 1 : end;
                    token.Append(line, i + 1, close - i - 1);
                    inToken = true;
                    plain = false;
                    i = end;
                    break;
                }

                case '"':
                    i = ReadDoubleQuoted(line, i, token);
                    inToken = true;
                    plain = false;
                    break;

                case '$' when next == '(':
                {
                    var end = SkipSubstitution(line, i);
                    token.Append(line, i, end - i);
                    inToken = true;
                    plain = false;
                    i = end;
                    break;
                }

                case '`':
                {
                    var end = SkipBackticks(line, i);
                    token.Append(line, i, end - i);
                    inToken = true;
                    plain = false;
                    i = end;
                    break;
                }

                case ' ' or '\t' or '\r':
                    EndToken();
                    i++;
                    break;

                case '#' when !inToken:
                {
                    // Comment: runs to the end of the line, connectors included.
                    var newline = line.IndexOf('\n', i);
                    if (newline < 0)
                    {
                        EndCommand(i, null);
                        commandStart = i = n;
                    }
                    else
                    {
                        EndCommand(i, Connector.Newline);
                        commandStart = i = newline + 1;
                    }
                    break;
                }

                case '>' or '<':
                {
                    var op = ReadRedirectOperator(line, i);
                    BeginRedirect(op, allowFd: true);
                    i += op.Length;
                    break;
                }

                case '&' when next == '>':
                {
                    var op = i + 2 < n && line[i + 2] == '>' ? "&>>" : "&>";
                    BeginRedirect(op, allowFd: false);
                    i += op.Length;
                    break;
                }

                case '\n':
                    EndCommand(i, Connector.Newline);
                    commandStart = ++i;
                    break;

                case ';':
                    EndCommand(i, Connector.Semicolon);
                    commandStart = ++i;
                    break;

                case '&' when next == '&':
                    EndCommand(i, Connector.And);
                    commandStart = i += 2;
                    break;

                case '&' when tokens.Count == 0 && !inToken && redirections.Count == 0 && redirect is null
                    && (next is ' ' or '\t' or '\'' or '"'):
                    // PowerShell call operator at the start of a command: "& 'C:\tools\x.exe' args".
                    // The program is whatever follows; the '&' itself is dropped.
                    callTarget = true;
                    i++;
                    break;

                case '&':
                    EndCommand(i, Connector.Background);
                    commandStart = ++i;
                    break;

                case '|' when next == '|':
                    EndCommand(i, Connector.Or);
                    commandStart = i += 2;
                    break;

                case '|' when next == '&':
                    // "|&" pipes stderr as well; still a pipe.
                    EndCommand(i, Connector.Pipe);
                    commandStart = i += 2;
                    break;

                case '|':
                    EndCommand(i, Connector.Pipe);
                    commandStart = ++i;
                    break;

                default:
                    token.Append(c);
                    inToken = true;
                    i++;
                    break;
            }
        }

        EndCommand(n, null);
        return commands;
    }

    private static bool IsAllDigits(StringBuilder token)
    {
        for (var i = 0; i < token.Length; i++)
        {
            if (!char.IsAsciiDigit(token[i]))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    // Starts at an unquoted '<' or '>' and returns the full operator, without
    // any file-descriptor prefix.
    private static string ReadRedirectOperator(string line, int start)
    {
        var a = start + 1 < line.Length ? line[start + 1] : '\0';
        var b = start + 2 < line.Length ? line[start + 2] : '\0';

        if (line[start] == '>')
        {
            return a switch
            {
                '>' => ">>",
                '&' => ">&",
                '|' => ">|",
                _ => ">",
            };
        }

        return a switch
        {
            '<' => b switch
            {
                '<' => "<<<",
                '-' => "<<-",
                _ => "<<",
            },
            '&' => "<&",
            '>' => "<>",
            _ => "<",
        };
    }

    // Each helper below takes the index of the opening delimiter and returns the
    // index just past the closing one (or line.Length if it is unterminated).

    private static int ReadDoubleQuoted(string line, int open, StringBuilder token)
    {
        var n = line.Length;
        var i = open + 1;
        while (i < n)
        {
            var c = line[i];
            var next = i + 1 < n ? line[i + 1] : '\0';

            if (c == '"')
            {
                return i + 1;
            }

            if (c == '\\' && i + 1 < n)
            {
                // Inside double quotes a backslash only escapes these characters;
                // before anything else it is kept literally.
                if (next is '$' or '`' or '"' or '\\')
                {
                    token.Append(next);
                    i += 2;
                }
                else if (next == '\n')
                {
                    i += 2;
                }
                else
                {
                    token.Append('\\');
                    i++;
                }
            }
            else if (c == '$' && next == '(')
            {
                var end = SkipSubstitution(line, i);
                token.Append(line, i, end - i);
                i = end;
            }
            else if (c == '`')
            {
                var end = SkipBackticks(line, i);
                token.Append(line, i, end - i);
                i = end;
            }
            else
            {
                token.Append(c);
                i++;
            }
        }

        return n;
    }

    private static int SkipSingleQuoted(string line, int open)
    {
        var close = line.IndexOf('\'', open + 1);
        return close < 0 ? line.Length : close + 1;
    }

    private static int SkipDoubleQuoted(string line, int open)
    {
        var n = line.Length;
        var i = open + 1;
        while (i < n)
        {
            switch (line[i])
            {
                case '"':
                    return i + 1;
                case '\\':
                    i += 2;
                    break;
                case '$' when i + 1 < n && line[i + 1] == '(':
                    i = SkipSubstitution(line, i);
                    break;
                case '`':
                    i = SkipBackticks(line, i);
                    break;
                default:
                    i++;
                    break;
            }
        }

        return n;
    }

    // Starts at the '$' of "$(" and matches parentheses, so nested substitutions
    // and "$(( ))" arithmetic are covered.
    private static int SkipSubstitution(string line, int start)
    {
        var n = line.Length;
        var depth = 0;
        var i = start + 1;
        while (i < n)
        {
            switch (line[i])
            {
                case '\\':
                    i += 2;
                    break;
                case '\'':
                    i = SkipSingleQuoted(line, i);
                    break;
                case '"':
                    i = SkipDoubleQuoted(line, i);
                    break;
                case '`':
                    i = SkipBackticks(line, i);
                    break;
                case '(':
                    depth++;
                    i++;
                    break;
                case ')':
                    i++;
                    if (--depth == 0)
                    {
                        return i;
                    }
                    break;
                default:
                    i++;
                    break;
            }
        }

        return n;
    }

    private static int SkipBackticks(string line, int open)
    {
        var n = line.Length;
        var i = open + 1;
        while (i < n)
        {
            if (line[i] == '\\')
            {
                i += 2;
            }
            else if (line[i] == '`')
            {
                return i + 1;
            }
            else
            {
                i++;
            }
        }

        return n;
    }
}
