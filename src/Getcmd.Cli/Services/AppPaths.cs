namespace Getcmd.Cli.Services;

/// <summary>Locations of getcmd's files: ~/.getcmd, or GETCMD_HOME when set.</summary>
internal sealed class AppPaths(string home)
{
    public string Home { get; } = home;

    public string ConfigFile => Path.Combine(Home, "config.json");

    public string LogDb => Path.Combine(Home, "log.db");

    public string HookErrorLog => Path.Combine(Home, "hook-errors.log");

    public static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static AppPaths Resolve()
    {
        var custom = Environment.GetEnvironmentVariable("GETCMD_HOME");
        return new AppPaths(string.IsNullOrEmpty(custom) ? Path.Combine(UserHome, ".getcmd") : custom);
    }

    public void EnsureHome() => Directory.CreateDirectory(Home);
}
