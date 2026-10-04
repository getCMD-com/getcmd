using Getcmd.Cli.Services;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd doctor: check that the installation is in working order.</summary>
internal static class DoctorCommand
{
    public static int Run(TextWriter stdout)
    {
        var paths = AppPaths.Resolve();
        var failed = false;

        void Check(string name, Func<string> probe)
        {
            string detail;
            var ok = true;
            try
            {
                detail = probe();
            }
            catch (Exception ex)
            {
                ok = false;
                detail = ex.Message;
            }

            failed |= !ok;
            stdout.WriteLine($"{(ok ? "OK  " : "FAIL")}  {name,-16} {detail}");
        }

        Check("binary on PATH", FindOnPath);

        Check("home writable", () =>
        {
            paths.EnsureHome();
            var probe = Path.Combine(paths.Home, $".doctor-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return paths.Home;
        });

        Check("log.db opens", () =>
        {
            using var log = DecisionLog.Open(paths);
            return paths.LogDb;
        });

        Check("config parses", () =>
        {
            try
            {
                ConfigService.LoadStrict(paths);
            }
            catch (Exception ex)
            {
                throw new FormatException($"{ex.Message} (defaults are in use until it is fixed)", ex);
            }

            return paths.ConfigFile;
        });

        Check("ask in auto mode", () => Names.Of(ConfigService.AskWhenAutoApproved(ConfigService.Load(paths))));

        Check("hook installed", () =>
        {
            var settings = ClaudeSettings.UserSettingsPath;
            var matchers = ClaudeSettings.MatchersFor(OperatingSystem.IsWindows());
            var missing = matchers.Where(matcher => !ClaudeSettings.IsInstalled(settings, matcher)).ToList();
            return missing.Count == 0
                ? $"{settings} ({string.Join(", ", matchers)})"
                : throw new InvalidOperationException(
                    $"no entry for {string.Join(", ", missing)} in {settings}; run: getcmd hook claude --install");
        });

        Check("version", () => AppInfo.Version);

        return failed ? 1 : 0;
    }

    private static string FindOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "getcmd.exe" : "getcmd";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{name} is not in any PATH directory");
    }
}
