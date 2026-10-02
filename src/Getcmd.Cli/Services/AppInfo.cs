using System.Reflection;

namespace Getcmd.Cli.Services;

internal static class AppInfo
{
    // Comes from <Version> in the project file.
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
