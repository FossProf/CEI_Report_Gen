namespace CEI.ReportGenerator.Core;

public static class WeatherOptions
{
    public static IReadOnlyList<string> All { get; } =
    [
        "Sunny",
        "Partly Cloudy",
        "Cloudy",
        "Mostly Cloudy",
        "Overcast",
        "Rainy",
        "Raining"
    ];

    public static bool IsValid(string? value)
        => value is not null && All.Contains(value, StringComparer.Ordinal);
}
