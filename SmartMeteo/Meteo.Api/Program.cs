// Program.cs
using System.Text.Json.Nodes;
using Serilog;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serilog configuration ----------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ---------- Swagger / OpenAPI with XML comments ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Meteo Station API",
        Version = "v1",
        Description = "API for retrieving and managing meteorological measurements."
    });

    // Include XML comments (make sure to enable XML documentation file in project properties)
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// ---------- App and data store ----------
var app = builder.Build();

// Measurement model
// The record type is defined in ProgramHelpers.cs.

// In-memory store: key = UTC DateTime truncated to minutes
var store = new Dictionary<DateTime, Measurement>();

// Seed fake data (last 10 minutes)
var nowUtc = DateTime.UtcNow;
for (int i = 0; i < 10; i++)
{
    var dt = Helpers.TruncateToMinutes(nowUtc.AddMinutes(-i));
    store[dt] = new Measurement(
        Temperature: 15 + i * 0.5,
        Pressure: 1010 + i * 0.8,
        Humidity: 60 - i * 0.7,
        WindSpeed: 3 + i * 0.2
    );
}

// ---------- Configuration: valid tokens ----------
var validTokens = new HashSet<string> { "secret-token-1", "another-valid-token" };

// ---------- Middlewares ----------

/// <summary>
/// Unified error handling middleware that returns JSON error responses.
/// </summary>
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Unhandled exception");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var err = new { error = "Internal server error", detail = ex.Message };
        await context.Response.WriteAsJsonAsync(err);
    }
});

/// <summary>
/// Simple request/response logging middleware using Serilog.
/// </summary>
app.Use(async (context, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Log.Information("Request {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
    sw.Stop();
    Log.Information("Response {StatusCode} for {Method} {Path} in {Elapsed}ms",
        context.Response.StatusCode, context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds);
});

/// <summary>
/// Validation middleware for query parameters and POST body shape.
/// Stores parsed values in HttpContext.Items for handlers.
/// </summary>
/// <summary>
/// Validation middleware for query parameters and POST body shape.
/// Stores parsed values in HttpContext.Items for handlers.
/// </summary>
app.Use(async (context, next) =>
{
    if (IsHistoryEndpoint(context))
    {
        if (!await ValidateAndStoreDateRange(context))
            return; // error already written
    }

    if (IsCalibrationEndpoint(context))
    {
        if (!await ValidateAndStoreCalibrationBody(context))
            return; // error already written
    }

    if (IsCurrentEndpoint(context))
    {
        if (!await ValidateCurrentRequest(context, store))
            return; // error already written
    }

    await next();
});


// ---------------- Helper functions ----------------

static bool IsHistoryEndpoint(HttpContext ctx) =>
    ctx.Request.Path.StartsWithSegments("/api/history", StringComparison.OrdinalIgnoreCase);

static bool IsCalibrationEndpoint(HttpContext ctx) =>
    ctx.Request.Path.Equals("/api/calibration", StringComparison.OrdinalIgnoreCase)
    && ctx.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

static bool IsCurrentEndpoint(HttpContext ctx) =>
    ctx.Request.Path.Equals("/api/current", StringComparison.OrdinalIgnoreCase)
    && ctx.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase);

static async Task<bool> ValidateAndStoreDateRange(HttpContext ctx)
{
    var q = ctx.Request.Query;
    DateTime? from = null, to = null;

    if (q.TryGetValue("from", out var fromVals))
    {
        if (!DateTime.TryParse(fromVals, out var parsedFrom))
        {
            await WriteError(ctx, "Invalid 'from' date format. Use ISO 8601.");
            return false;
        }
        from = parsedFrom.ToUniversalTime();
    }

    if (q.TryGetValue("to", out var toVals))
    {
        if (!DateTime.TryParse(toVals, out var parsedTo))
        {
            await WriteError(ctx, "Invalid 'to' date format. Use ISO 8601.");
            return false;
        }
        to = parsedTo.ToUniversalTime();
    }

    if (from.HasValue && to.HasValue && from > to)
    {
        await WriteError(ctx, "'from' must be less than or equal to 'to'.");
        return false;
    }

    ctx.Items["range_from"] = from;
    ctx.Items["range_to"]   = to;
    return true;
}

static async Task<bool> ValidateAndStoreCalibrationBody(HttpContext ctx)
{
    if (!ctx.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
    {
        await WriteError(ctx, "Content-Type must be application/json");
        return false;
    }

    ctx.Request.EnableBuffering();
    using var sr = new StreamReader(ctx.Request.Body, leaveOpen: true);
    var bodyText = await sr.ReadToEndAsync();
    ctx.Request.Body.Position = 0;

    if (string.IsNullOrWhiteSpace(bodyText))
    {
        await WriteError(ctx, "Empty request body");
        return false;
    }

    JsonNode? node;
    try { node = JsonNode.Parse(bodyText); }
    catch { await WriteError(ctx, "Invalid JSON body"); return false; }

    if (node is not JsonObject obj)
    {
        await WriteError(ctx, "Expected JSON object at root");
        return false;
    }

    foreach (var kv in obj)
    {
        if (kv.Value is not JsonArray arr)
        {
            await WriteError(ctx, $"Parameter '{kv.Key}' must be an array of numbers");
            return false;
        }
        foreach (var item in arr)
        {
            if (!double.TryParse(item?.ToJsonString(), out _))
            {
                await WriteError(ctx, $"Parameter '{kv.Key}' contains non-numeric value");
                return false;
            }
        }
    }

    ctx.Items["calibration_body"] = obj;
    return true;
}

static async Task<bool> ValidateCurrentRequest(HttpContext ctx, Dictionary<DateTime, Measurement>? store)
{
    // Example validation: ensure store has data
    if (store?.Count == 0)
    {
        await WriteError(ctx, "No measurements available");
        return false;
    }

    // Optional: check for query parameter "strict=true"
    var strict = ctx.Request.Query.TryGetValue("strict", out var strictVal) &&
                 bool.TryParse(strictVal, out var strictFlag) && strictFlag;

    if (strict)
    {
        var latest = store.Keys.OrderByDescending(k => k).First();
        if ((DateTime.UtcNow - latest).TotalMinutes > 30)
        {
            await WriteError(ctx, "Latest measurement is too old");
            return false;
        }
    }

    return true;
}

static async Task WriteError(HttpContext ctx, string message)
{
    ctx.Response.StatusCode = 400;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}


/// <summary>
/// Authentication middleware for POST /api/calibration that validates Bearer tokens.
/// Returns 401 for missing or invalid tokens.
/// </summary>
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/api/calibration", StringComparison.OrdinalIgnoreCase)
        && context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Missing Authorization header" });
            return;
        }

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid Authorization header format" });
            return;
        }

        var token = header.Substring("Bearer ".Length).Trim();
        if (!validTokens.Contains(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid token" });
            return;
        }

        // token valid -> proceed
        context.Items["authenticated_token"] = token;
    }

    await next();
});

// ---------- Enable Swagger UI ----------
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Meteo Station API v1");
    c.RoutePrefix = "swagger";
});

// ---------- Endpoints implemented as named methods so XML comments are picked up by Swagger ----------

/// <summary>
/// Returns the most recent meteorological measurement.
/// </summary>
/// <remarks>
/// This endpoint retrieves the latest measurement from the store.  
/// Example response:
/// {
///   "timestamp": "2026-06-07T20:00:00Z",
///   "temperature": 22.5,
///   "pressure": 1012,
///   "humidity": 60,
///   "windSpeed": 5.4
/// }
/// </remarks>
/// <returns>JSON object with the latest measurement values</returns>
app.MapGet("/api/current", () =>
{
    if (store?.Count == 0)
    {
        return Results.NotFound(new { error = "No measurements available" });
    }

    // Get the latest measurement by timestamp
    var latest = store.OrderByDescending(kv => kv.Key).First();

    var response = new
    {
        timestamp   = latest.Key.ToString("yyyy-MM-ddTHH:mm:00Z"),
        temperature = latest.Value.Temperature,
        pressure    = latest.Value.Pressure,
        humidity    = latest.Value.Humidity,
        windSpeed   = latest.Value.WindSpeed
    };

    return Results.Json(response);
});


/// <summary>
/// Returns the latest measurement values.
/// </summary>
/// <remarks>
/// Response is a JSON array of objects with properties 'name' and 'value'.
/// Example:
/// [ { "name": "temperature", "value": 15.5 }, { "name": "pressure", "value": 1012.3 } ]
/// </remarks>
/// <returns>JSON array of measurement name/value pairs</returns>
app.MapGet("/api/history", (HttpContext ctx) =>
{
    // Read query parameters directly
    var fromStr = ctx.Request.Query["range_from"];
    var toStr   = ctx.Request.Query["range_to"];

    DateTime? from = DateTime.TryParse(fromStr, out var f) ? f : null;
    DateTime? to   = DateTime.TryParse(toStr, out var t) ? t : null;

    var query = store.AsEnumerable();
    if (from.HasValue) query = query.Where(kv => kv.Key >= Helpers.TruncateToMinutes(from.Value));
    if (to.HasValue)   query = query.Where(kv => kv.Key <= Helpers.TruncateToMinutes(to.Value));

    var result = query
        .OrderBy(kv => kv.Key)
        .ToDictionary(
            kv => kv.Key.ToString("yyyy-MM-ddTHH:mm:00Z"),
            kv => new {
                temperature = kv.Value.Temperature,
                pressure    = kv.Value.Pressure,
                humidity    = kv.Value.Humidity,
                windSpeed   = kv.Value.WindSpeed
            }
        );

    return Results.Json(result);
});

/// <summary>
/// Deletes historical measurements in an optional date range.
/// </summary>
/// <param name="ctx">HttpContext used to read optional parsed 'from' and 'to' range stored by validation middleware</param>
/// <returns>JSON object with counts of removed and remaining entries</returns>
app.MapDelete("/api/history", (HttpContext ctx) =>
{
    // Read query parameters
    var fromStr = ctx.Request.Query["range_from"];
    var toStr   = ctx.Request.Query["range_to"];

    DateTime? from = DateTime.TryParse(fromStr, out var f) ? f : null;
    DateTime? to   = DateTime.TryParse(toStr, out var t) ? t : null;

    // Select keys to remove
    var keysToRemove = store.Keys.AsEnumerable();
    if (from.HasValue) keysToRemove = keysToRemove.Where(k => k >= Helpers.TruncateToMinutes(from.Value));
    if (to.HasValue)   keysToRemove = keysToRemove.Where(k => k <= Helpers.TruncateToMinutes(to.Value));

    var removed = keysToRemove.ToList();
    foreach (var k in removed)
    {
        store.Remove(k);
    }

    // Return a friendly JSON summary
    return Results.Json(new
    {
        removedCount = removed.Count,
        remainingCount = store.Count,
        message = $"Removed {removed.Count} entries. {store.Count} remain."
    });
});

/// <summary>
/// Accepts calibration data and returns the parsed calibration dictionary.
/// </summary>
/// <remarks>
/// Protected by Bearer token authentication.  
/// The request body must be a JSON object where each key is a parameter name and each value is an array of numeric calibration values.
/// Example:
/// {
///   "temperature": [0.1, -0.2],
///   "pressure": [1.0]
/// }
/// </remarks>
/// <returns>JSON dictionary where key is parameter name and value is array of numbers</returns>
app.MapPost("/api/calibration", async (HttpContext ctx) =>
{
    try
    {
        // Read and parse JSON body
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();

        var bodyObj = JsonNode.Parse(body) as JsonObject;
        if (bodyObj is null)
        {
            return Results.BadRequest(new { error = "Invalid or missing calibration body" });
        }

        // Convert to dictionary of double arrays
        var response = new Dictionary<string, double[]>();
        foreach (var kv in bodyObj)
        {
            var arr = kv.Value!.AsArray();
            var numbers = arr.Select(n => double.Parse(n!.ToJsonString())).ToArray();
            response[kv.Key] = numbers;
        }

        return Results.Json(response);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Failed to parse calibration data: {ex.Message}" });
    }
});

app.Run();
