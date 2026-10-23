using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNetForge.Sdk;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WeatherWidget;

/// <summary>
/// Entry point of the Weather widget. The tile is <c>Views/Widget.cshtml</c>, which the host renders on the admin
/// dashboard; this class registers the <see cref="WeatherService"/> the view injects.
/// </summary>
public sealed class WeatherWidgetExtension : IExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        services.AddHttpClient<WeatherService>(client =>
        {
            client.BaseAddress = new Uri("https://api.open-meteo.com/");
            client.Timeout = TimeSpan.FromSeconds(4);
        });
        services.AddSingleton(new WeatherOptions(
            context.GetString("city", "Pristina")!,
            context.GetDouble("latitude", 42.6629),
            context.GetDouble("longitude", 21.1655),
            string.Equals(context.GetString("units"), "imperial", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed record WeatherOptions(string City, double Latitude, double Longitude, bool Imperial);

public sealed record WeatherReport(string City, double Temperature, string TemperatureUnit, double Wind, string WindUnit,
    string Summary, DateTime ObservedLocal);

/// <summary>
/// Current conditions from Open-Meteo, cached for 15 minutes so the dashboard doesn't call the API on every view.
/// Returns <c>null</c> when the service can't be reached - the widget then says so instead of failing.
/// </summary>
public sealed class WeatherService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;
    private readonly WeatherOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WeatherService> _logger;

    public WeatherService(HttpClient http, WeatherOptions options, IMemoryCache cache, ILogger<WeatherService> logger)
    {
        _http = http;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public WeatherOptions Options => _options;

    public async Task<WeatherReport?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        const string key = "weather-widget:current";
        if (_cache.TryGetValue(key, out WeatherReport? cached))
        {
            return cached;
        }

        var units = _options.Imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : string.Empty;
        var url = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={_options.Latitude}&longitude={_options.Longitude}&current=temperature_2m,weather_code,wind_speed_10m&timezone=auto{units}");
        try
        {
            var response = await _http.GetFromJsonAsync<Forecast>(url, cancellationToken);
            if (response?.Current is not { } current)
            {
                return null;
            }

            var report = new WeatherReport(_options.City, current.Temperature, _options.Imperial ? "°F" : "°C",
                current.Wind, _options.Imperial ? "mph" : "km/h", Describe(current.WeatherCode), current.Time);
            _cache.Set(key, report, CacheFor);
            return report;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Weather for {City} is unavailable.", _options.City);
            return null;
        }
    }

    /// <summary>WMO weather interpretation codes (https://open-meteo.com/en/docs).</summary>
    private static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        85 or 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "Unknown",
    };

    private sealed record Forecast([property: JsonPropertyName("current")] CurrentWeather? Current);

    private sealed record CurrentWeather(
        [property: JsonPropertyName("time")] DateTime Time,
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double Wind);
}
