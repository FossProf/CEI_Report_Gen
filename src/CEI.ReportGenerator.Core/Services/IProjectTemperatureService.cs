using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public interface IProjectTemperatureService
{
    Task<TemperatureLookupResult> GetCurrentTemperatureAsync(
        ProjectCoordinates coordinates,
        CancellationToken cancellationToken);

    Task<TemperatureLookupResult> GetHistoricalDaytimeAverageAsync(
        ProjectCoordinates coordinates,
        DateTime date,
        int startHour,
        int endHour,
        CancellationToken cancellationToken);
}
