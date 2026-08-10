namespace CEI.ReportGenerator.Core.Services;

public static class HistoricalTemperatureAverager
{
    public static TemperatureLookupResult AverageFahrenheit(IEnumerable<double> temperaturesFahrenheit)
    {
        var values = temperaturesFahrenheit.ToList();
        if (values.Count == 0)
        {
            return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
        }

        return TemperatureLookupResult.Success(values.Average());
    }
}
