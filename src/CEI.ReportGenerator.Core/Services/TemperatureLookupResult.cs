namespace CEI.ReportGenerator.Core.Services;

public sealed record TemperatureLookupResult(double? TemperatureFahrenheit, string? FailureMessage = null)
{
    public bool IsSuccess => TemperatureFahrenheit.HasValue;

    public int? RoundedTemperatureFahrenheit
        => TemperatureFahrenheit.HasValue
            ? (int)Math.Round(TemperatureFahrenheit.Value, MidpointRounding.AwayFromZero)
            : null;

    public static TemperatureLookupResult Failure(string message)
        => new(null, message);

    public static TemperatureLookupResult Success(double temperatureFahrenheit)
        => new(temperatureFahrenheit);
}
