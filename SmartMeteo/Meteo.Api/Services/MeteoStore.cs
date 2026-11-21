namespace Meteo.Api.Services;

/// <summary>Thread-safe in-memory storage for meteorological measurements.</summary>
public sealed class MeteoStore
{
    private readonly Dictionary<DateTime, Measurement> _store = [];

    public MeteoStore()
    {
        var nowUtc = DateTime.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            var timestamp = Helpers.TruncateToMinutes(nowUtc.AddMinutes(-i));
            _store[timestamp] = new Measurement(
                Temperature: 15 + i * 0.5,
                Pressure: 1010 + i * 0.8,
                Humidity: 60 - i * 0.7,
                WindSpeed: 3 + i * 0.2);
        }
    }

    public IReadOnlyDictionary<DateTime, Measurement> Measurements => _store;

    public void Remove(IEnumerable<DateTime> timestamps)
    {
        foreach (var timestamp in timestamps)
            _store.Remove(timestamp);
    }
}
