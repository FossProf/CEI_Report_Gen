using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.Services;

public sealed class OpenMeteoProjectTemperatureService(
    HttpClient httpClient,
    TimeProvider? timeProvider = null) : IProjectTemperatureService
{
    // Current-temperature results expire after 10 minutes so "current" remains meaningfully current.
    private static readonly TimeSpan CurrentTemperatureCacheTtl = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, CachedCurrentTemperatureResult> _currentCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TemperatureLookupResult> _historicalCache = new(StringComparer.Ordinal);

    public async Task<TemperatureLookupResult> GetCurrentTemperatureAsync(
        ProjectCoordinates coordinates,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"current|{coordinates.Latitude:F4}|{coordinates.Longitude:F4}|{coordinates.TimeZoneId}";
        if (_currentCache.TryGetValue(cacheKey, out var cached)
            && _timeProvider.GetUtcNow() - cached.CachedUtc < CurrentTemperatureCacheTtl)
        {
            return cached.Result;
        }

        _currentCache.TryRemove(cacheKey, out _);

        try
        {
            var url =
                $"https://api.open-meteo.com/v1/forecast?latitude={coordinates.Latitude}&longitude={coordinates.Longitude}&current=temperature_2m&temperature_unit=fahrenheit&timezone={Uri.EscapeDataString(coordinates.TimeZoneId)}";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return TemperatureLookupResult.Failure($"Temperature lookup unavailable. Enter temperature manually. (HTTP {(int)response.StatusCode})");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<ForecastResponse>(stream, cancellationToken: cancellationToken);
            if (payload?.Current?.Temperature2m is not double value)
            {
                return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
            }

            var result = TemperatureLookupResult.Success(value);
            _currentCache[cacheKey] = new CachedCurrentTemperatureResult(result, _timeProvider.GetUtcNow());
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Current temperature lookup failed: {ex.Message}");
            return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
        }
    }

    public async Task<TemperatureLookupResult> GetHistoricalDaytimeAverageAsync(
        ProjectCoordinates coordinates,
        DateTime date,
        int startHour,
        int endHour,
        CancellationToken cancellationToken)
    {
        var cacheKey =
            $"historical|{coordinates.Latitude:F4}|{coordinates.Longitude:F4}|{coordinates.TimeZoneId}|{date:yyyy-MM-dd}|{startHour}|{endHour}";
        if (_historicalCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var isoDate = date.ToString("yyyy-MM-dd");
            var url =
                $"https://archive-api.open-meteo.com/v1/archive?latitude={coordinates.Latitude}&longitude={coordinates.Longitude}&start_date={isoDate}&end_date={isoDate}&hourly=temperature_2m&temperature_unit=fahrenheit&timezone={Uri.EscapeDataString(coordinates.TimeZoneId)}";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return TemperatureLookupResult.Failure($"Temperature lookup unavailable. Enter temperature manually. (HTTP {(int)response.StatusCode})");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<HistoricalResponse>(stream, cancellationToken: cancellationToken);
            if (payload?.Hourly?.Time is null || payload.Hourly.Temperature2m is null)
            {
                return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
            }

            var values = payload.Hourly.Time
                .Zip(payload.Hourly.Temperature2m, (timeText, temperature) => new { timeText, temperature })
                .Where(entry => DateTime.TryParse(entry.timeText, out _))
                .Select(entry => new { Time = DateTime.Parse(entry.timeText), entry.temperature })
                .Where(entry => entry.Time.Hour >= startHour && entry.Time.Hour <= endHour)
                .Select(entry => entry.temperature)
                .ToList();

            if (values.Count == 0)
            {
                return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
            }

            var result = HistoricalTemperatureAverager.AverageFahrenheit(values);
            _historicalCache[cacheKey] = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Historical temperature lookup failed: {ex.Message}");
            return TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually.");
        }
    }

    private sealed class ForecastResponse
    {
        [JsonPropertyName("current")]
        public CurrentWeatherPayload? Current { get; set; }
    }

    private sealed class CurrentWeatherPayload
    {
        [JsonPropertyName("temperature_2m")]
        public double? Temperature2m { get; set; }
    }

    private sealed class HistoricalResponse
    {
        [JsonPropertyName("hourly")]
        public HistoricalHourlyPayload? Hourly { get; set; }
    }

    private sealed class HistoricalHourlyPayload
    {
        [JsonPropertyName("time")]
        public List<string>? Time { get; set; }

        [JsonPropertyName("temperature_2m")]
        public List<double>? Temperature2m { get; set; }
    }

    private sealed record CachedCurrentTemperatureResult(
        TemperatureLookupResult Result,
        DateTimeOffset CachedUtc);
}
