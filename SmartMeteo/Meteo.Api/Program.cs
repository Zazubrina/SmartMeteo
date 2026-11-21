// Program.cs
using Serilog;
using Microsoft.OpenApi;
using Meteo.Api.Authentication;
using Meteo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.MaxDepth = 16;
});

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

// ---------- Authentication, authorization, and secure validation ----------
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddValidation();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CalibrationAdmin", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole("admin"));
});

// ---------- App and data store ----------
builder.Services.AddControllers();
builder.Services.AddSingleton<MeteoStore>();

// ---------- App ----------
var app = builder.Build();

// Measurement model
// The record type is defined in ProgramHelpers.cs.

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
        var err = new { error = "Internal server error" };
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

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

    if (context.Request.Path.StartsWithSegments("/api/auth/login"))
        context.Response.Headers.CacheControl = "no-store";

    await next();
});

app.UseRateLimiter();

// ---------- Enable Swagger UI ----------
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Meteo Station API v1");
    c.RoutePrefix = "swagger";
});

app.MapAuthenticationEndpoints();
app.MapControllers();

app.Run();

public partial class Program;
