using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.Services;

public sealed class OpenMeteoProjectLocationResolver(HttpClient httpClient) : IProjectLocationResolver
{
    public async Task<ProjectCoordinates?> ResolveAsync(string locationText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(locationText))
        {
            return null;
        }

        try
        {
            var url =
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(locationText.Trim())}&count=1&language=en&format=json";
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Trace.WriteLine($"Temperature assistance geocoding failed with HTTP {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<GeocodingResponse>(stream, cancellationToken: cancellationToken);
            var first = payload?.Results?.FirstOrDefault();
            if (first is null || string.IsNullOrWhiteSpace(first.Timezone))
            {
                return null;
            }

            return new ProjectCoordinates(first.Latitude, first.Longitude, first.Timezone);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Temperature assistance geocoding failed: {ex.Message}");
            return null;
        }
    }

    private sealed class GeocodingResponse
    {
        public List<GeocodingResult>? Results { get; set; }
    }

    private sealed class GeocodingResult
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        [JsonPropertyName("timezone")]
        public string Timezone { get; set; } = string.Empty;
    }
}
