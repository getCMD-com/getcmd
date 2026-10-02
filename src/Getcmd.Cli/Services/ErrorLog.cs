namespace Getcmd.Cli.Services;

/// <summary>Appends failures to ~/.getcmd/hook-errors.log. Never throws.</summary>
internal static class ErrorLog
{
    public static void Write(AppPaths paths, Exception exception)
    {
        try
        {
            paths.EnsureHome();
            File.AppendAllText(
                paths.HookErrorLog,
                $"{DecisionLog.Timestamp(DateTime.UtcNow)} {exception}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere left to report it.
        }
    }
}
