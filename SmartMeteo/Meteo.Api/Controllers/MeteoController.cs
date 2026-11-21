using System.Text.Json.Nodes;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Meteo.Api.Services;
using Meteo.Api.Validation;

namespace Meteo.Api.Controllers;

/// <summary>Provides meteorological measurement endpoints.</summary>
[ApiController]
[Route("api")]
public sealed class MeteoController(MeteoStore store) : ControllerBase
{
    /// <summary>Returns the most recent meteorological measurement.</summary>
    [HttpGet("current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetCurrent()
    {
        if (store.Measurements.Count == 0)
            return NotFound(new { error = "No measurements available" });

        var latest = store.Measurements.MaxBy(item => item.Key);

        return Ok(new
        {
            timestamp = latest.Key.ToString("yyyy-MM-ddTHH:mm:00Z"),
            temperature = latest.Value.Temperature,
            pressure = latest.Value.Pressure,
            humidity = latest.Value.Humidity,
            windSpeed = latest.Value.WindSpeed
        });
    }

    /// <summary>Returns historical measurements in an optional date range.</summary>
    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult GetHistory([FromQuery] HistoryRangeRequest range)
    {
        if (!SecureInput.TryParseUtcDate(range.From, out var from) ||
            !SecureInput.TryParseUtcDate(range.To, out var to))
            return BadRequest(new { error = "Invalid date format. Use ISO 8601." });

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest(new { error = "'from' must be less than or equal to 'to'." });

        var query = store.Measurements.AsEnumerable();
        if (from.HasValue)
            query = query.Where(item => item.Key >= Helpers.TruncateToMinutes(from.Value));
        if (to.HasValue)
            query = query.Where(item => item.Key <= Helpers.TruncateToMinutes(to.Value));

        var result = query
            .OrderBy(item => item.Key)
            .ToDictionary(
                item => item.Key.ToString("yyyy-MM-ddTHH:mm:00Z"),
                item => new
                {
                    temperature = item.Value.Temperature,
                    pressure = item.Value.Pressure,
                    humidity = item.Value.Humidity,
                    windSpeed = item.Value.WindSpeed
                });

        return Ok(result);
    }

    /// <summary>Deletes historical measurements in an optional date range.</summary>
    [HttpDelete("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult DeleteHistory([FromQuery] HistoryRangeRequest range)
    {
        if (!SecureInput.TryParseUtcDate(range.From, out var from) ||
            !SecureInput.TryParseUtcDate(range.To, out var to))
            return BadRequest(new { error = "Invalid date format. Use ISO 8601." });

        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return BadRequest(new { error = "'from' must be less than or equal to 'to'." });

        var timestamps = store.Measurements.Keys.AsEnumerable();
        if (from.HasValue)
            timestamps = timestamps.Where(timestamp => timestamp >= Helpers.TruncateToMinutes(from.Value));
        if (to.HasValue)
            timestamps = timestamps.Where(timestamp => timestamp <= Helpers.TruncateToMinutes(to.Value));

        var removed = timestamps.ToList();
        store.Remove(removed);

        return Ok(new
        {
            removedCount = removed.Count,
            remainingCount = store.Measurements.Count,
            message = $"Removed {removed.Count} entries. {store.Measurements.Count} remain."
        });
    }

    /// <summary>Accepts calibration data and returns the parsed calibration dictionary.</summary>
    [HttpPost("calibration")]
    [Authorize(Policy = "CalibrationAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult PostCalibration([FromBody] JsonObject body)
    {
        if (!SecureInput.ValidateCalibrationBody(body, out var validationError))
            return BadRequest(new { error = validationError });

        var response = new Dictionary<string, double[]>();

        foreach (var item in body)
        {
            var numbers = item.Value!.AsArray()
                .Select(value => double.Parse(value!.ToJsonString(), CultureInfo.InvariantCulture))
                .ToArray();
            response[item.Key] = numbers;
        }

        return Ok(response);
    }
}
