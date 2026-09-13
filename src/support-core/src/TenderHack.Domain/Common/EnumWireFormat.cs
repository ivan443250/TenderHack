using System.Text.Json;

namespace TenderHack.Domain.Common;

/// <summary>
/// Converts an enum value to the SCREAMING_SNAKE_CASE spelling used on the wire
/// (docs/contracts/web-api-v0.md, e.g. "SIMULATED_ACCEPTED"). Manually-built event/notification/quality
/// payloads (<see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>-typed, not a DTO run through
/// the API's JSON serializer) must use this instead of bare <c>.ToString()</c>, which emits the C#
/// PascalCase member name and drifts from the contract (e.g. "SimulatedAccepted").
/// </summary>
public static class EnumWireFormat
{
    public static string ToWire<TEnum>(this TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

    public static string? ToWire<TEnum>(this TEnum? value) where TEnum : struct, Enum =>
        value is null ? null : ToWire(value.Value);
}
