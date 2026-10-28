namespace BackMaster.Core.Scheduling;

/// <summary>
/// Accepts either an IANA id ("Europe/Sofia") or a Windows id ("FLE Standard Time")
/// on any OS, so the same policy file works on every classroom PC and in tests.
/// </summary>
public static class TimeZoneResolver
{
    public static bool TryResolve(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;
        id = id.Trim();

        if (TryFind(id, out zone)) return true;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) && TryFind(windowsId, out zone))
            return true;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) && TryFind(ianaId, out zone))
            return true;

        zone = TimeZoneInfo.Utc;
        return false;
    }

    public static TimeZoneInfo Resolve(string? id) =>
        TryResolve(id, out var zone)
            ? zone
            : throw new TimeZoneNotFoundException($"Unknown time zone '{id}'.");

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
