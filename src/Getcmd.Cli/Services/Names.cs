using Getcmd.Core;
using Action = Getcmd.Core.Action;

namespace Getcmd.Cli.Services;

/// <summary>Lower-case names for levels and actions, as used in config.json, log.db and output.</summary>
internal static class Names
{
    public static readonly string[] Levels = ["read", "mutate", "egress", "secrets", "destructive"];
    public static readonly string[] Actions = ["allow", "ask", "block"];

    public static string Of(Level level) => Levels[(int)level];

    public static string Of(Action action) => Actions[(int)action];

    public static bool TryParseLevel(string? text, out Level level)
    {
        var index = IndexOf(Levels, text);
        level = (Level)Math.Max(index, 0);
        return index >= 0;
    }

    public static bool TryParseAction(string? text, out Action action)
    {
        var index = IndexOf(Actions, text);
        action = (Action)Math.Max(index, 0);
        return index >= 0;
    }

    private static int IndexOf(string[] names, string? text)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
