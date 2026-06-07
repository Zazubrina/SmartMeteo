using System;

public record Measurement(double Temperature, double Pressure, double Humidity, double WindSpeed);

public static class Helpers
{
    public static DateTime TruncateToMinutes(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }
}
