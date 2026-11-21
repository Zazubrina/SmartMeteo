using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Globalization;

namespace Meteo.Api.Validation;

/// <summary>Validated date range and strictness options for history requests.</summary>
public sealed record HistoryRangeRequest
{
    [StringLength(64)]
    public string? From { get; init; }

    [StringLength(64)]
    public string? To { get; init; }

    public bool Strict { get; init; }
}

/// <summary>Validated calibration payload containing named numeric arrays.</summary>
public sealed class CalibrationPayload
{
    [Required]
    [MaxLength(32)]
    public Dictionary<string, double[]> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class SecureInput
{
    private static readonly HashSet<string> AllowedParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "temperature", "pressure", "humidity", "windSpeed"
    };

    public static bool TryParseUtcDate(string? value, out DateTime? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Length > 64 || !DateTime.TryParseExact(
                value,
                new[] { "O", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ssK" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return false;

        result = parsed.ToUniversalTime();
        return true;
    }

    public static bool ValidateCalibrationBody(JsonObject body, out string error)
    {
        error = string.Empty;

        if (body.Count == 0)
        {
            error = "Calibration body must contain at least one parameter";
            return false;
        }

        if (body.Count > 32)
        {
            error = "Calibration body contains too many parameters";
            return false;
        }

        foreach (var item in body)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 32)
            {
                error = "Invalid calibration parameter name";
                return false;
            }

            if (!AllowedParameterNames.Contains(item.Key))
            {
                error = "Unknown calibration parameter";
                return false;
            }

            if (item.Value is not JsonArray values || values.Count == 0)
            {
                error = $"Parameter '{item.Key}' must be a non-empty array of numbers";
                return false;
            }

            if (values.Count > 100)
            {
                error = $"Parameter '{item.Key}' contains too many values";
                return false;
            }

            foreach (var value in values)
            {
                if (!double.TryParse(
                        value?.ToJsonString(),
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out var number) ||
                    double.IsFinite(number) is false)
                {
                    error = $"Parameter '{item.Key}' contains an invalid numeric value";
                    return false;
                }

                if (Math.Abs(number) > 1_000_000d)
                {
                    error = $"Parameter '{item.Key}' contains an out-of-range numeric value";
                    return false;
                }
            }
        }

        return true;
    }
}
