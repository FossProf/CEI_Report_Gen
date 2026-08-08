using System.Reflection;

namespace CEI.ReportGenerator.App;

internal static class AppInfo
{
    public static string GetDisplayVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "Unknown";
    }

    public static string GetAboutText()
        => $"CEI Report Generator{Environment.NewLine}" +
           $"Version {GetDisplayVersion()}{Environment.NewLine}" +
           ".NET desktop application for standardized CEI inspection reports.";
}
