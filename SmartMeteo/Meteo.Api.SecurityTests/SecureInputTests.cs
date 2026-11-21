using System.Text.Json.Nodes;
using Meteo.Api.Validation;

namespace Meteo.Api.SecurityTests;

public sealed class SecureInputTests
{
    [Theory]
    [InlineData("2026-06-07T20:00:00Z")]
    [InlineData("2026-06-07T20:00:00+00:00")]
    [InlineData(null)]
    public void TryParseUtcDate_AcceptsOnlySafeDates(string? value)
    {
        Assert.True(SecureInput.TryParseUtcDate(value, out _));
    }

    [Theory]
    [InlineData("2026-06-07') OR 1=1--")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("not-a-date")]
    public void TryParseUtcDate_RejectsInvalidOrInjectionInput(string value)
    {
        Assert.False(SecureInput.TryParseUtcDate(value, out _));
    }

    [Fact]
    public void ValidateCalibrationBody_AcceptsKnownFiniteNumericParameters()
    {
        var body = JsonNode.Parse("{\"temperature\":[20.5,21.0],\"pressure\":[1012.3]}")!.AsObject();

        Assert.True(SecureInput.ValidateCalibrationBody(body, out var error));
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("{\"temperature\":[1e1000]}" )]
    [InlineData("{\"unknown\":[1]}")]
    [InlineData("{\"temperature\":\"1\"}")]
    [InlineData("{\"temperature\":[1000001]}")]
    [InlineData("{}" )]
    public void ValidateCalibrationBody_RejectsUnsafeValues(string json)
    {
        var body = JsonNode.Parse(json)!.AsObject();

        Assert.False(SecureInput.ValidateCalibrationBody(body, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidateCalibrationBody_DoesNotEchoUntrustedParameterName()
    {
        var body = JsonNode.Parse("{\"<script>alert(1)</script>\":[1]}")!.AsObject();

        Assert.False(SecureInput.ValidateCalibrationBody(body, out var error));
        Assert.DoesNotContain("<script>", error, StringComparison.OrdinalIgnoreCase);
    }
}
