using System.Text.Json;
using Permixa.Domain.Audit;

namespace Permixa.Application.Audit;

/// <summary>
/// Serializes audit metadata to a bounded JSON object of string primitives only.
/// </summary>
public static class IamAuditMetadataSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    public static string? Serialize(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(metadata, Options);
        if (json.Length > IamAuditLog.MaxMetadataJsonLength)
        {
            throw new InvalidOperationException(
                $"Audit metadata exceeds the maximum of {IamAuditLog.MaxMetadataJsonLength} characters.");
        }

        return json;
    }

    public static IReadOnlyDictionary<string, string>? Deserialize(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson, Options);
        return parsed is { Count: > 0 } ? parsed : null;
    }

    public static string ToPersistedOutcome(IamAuditOutcome outcome) =>
        outcome switch
        {
            IamAuditOutcome.Success => "Success",
            IamAuditOutcome.Failure => "Failure",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };

    public static IamAuditOutcome ParseOutcome(string outcome) =>
        outcome switch
        {
            "Success" => IamAuditOutcome.Success,
            "Failure" => IamAuditOutcome.Failure,
            _ => throw new InvalidOperationException($"Unknown audit outcome '{outcome}'.")
        };
}
