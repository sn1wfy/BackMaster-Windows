using System.Text.Json;
using System.Text.Json.Serialization;
using BackMaster.Core.Scheduling;

namespace BackMaster.Core.Ipc;

/// <summary>
/// The tray talks to the service over a local named pipe, one JSON object per line in
/// each direction. The service never trusts the tray: it only answers questions and
/// checks phrases; every change to the machine needs an administrator.
/// </summary>
public static class ServiceProtocol
{
    public const string PipeName = "BackMaster.Service";

    /// <summary>Longest request line the server reads; anything longer is dropped.</summary>
    public const int MaxRequestChars = 4096;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Encode<T>(T message) => JsonSerializer.Serialize(message, JsonOptions);

    public static T? Decode<T>(string line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class Commands
{
    public const string Status = "status";
    public const string Unlock = "unlock";
}

public sealed record ServiceRequest(string Command, string? Phrase = null);

public enum UnlockResult
{
    Accepted,
    Rejected,
    /// <summary>Too many wrong guesses; see <see cref="ServiceResponse.RetryAfterSeconds"/>.</summary>
    LockedOut,
    /// <summary>No phrase has been set yet; an administrator must set one first.</summary>
    NotConfigured,
}

public sealed record ServiceResponse(
    bool Ok,
    string? Error = null,
    ServiceStatus? Status = null,
    UnlockResult? Unlock = null,
    int? RetryAfterSeconds = null);

/// <param name="PolicyError">Set when policy.json could not be read; the service keeps the last good policy.</param>
public sealed record ServiceStatus(
    bool Enforce,
    EnforcementMode Mode,
    DateTimeOffset? NextChangeUtc,
    string TimeZone,
    int BlockedDomainCount,
    string Reason,
    string? PolicyError = null);
