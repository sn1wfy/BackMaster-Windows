using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BackMaster.Core.Maintenance;

/// <summary>Reads and validates install manifests. Every rejection says what is wrong.</summary>
public static partial class ManifestLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static InstallManifest Load(string path) => Parse(File.ReadAllText(path));

    public static InstallManifest Parse(string json)
    {
        InstallManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<InstallManifest>(json, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The install manifest is not valid JSON: {e.Message}", e);
        }
        if (m is null) throw new InvalidDataException("The install manifest is empty.");
        Validate(m);
        return m;
    }

    public static string Serialize(InstallManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions);

    public static void Validate(InstallManifest m)
    {
        var errors = new List<string>();

        if (m.SchemaVersion != InstallManifest.CurrentSchemaVersion)
            errors.Add($"schemaVersion {m.SchemaVersion} is not supported (expected {InstallManifest.CurrentSchemaVersion}).");
        if (string.IsNullOrWhiteSpace(m.ProductVersion))
            errors.Add("productVersion is missing.");
        if (!IsAbsoluteWindowsPath(m.InstallDir))
            errors.Add($"installDir '{m.InstallDir}' must be an absolute path such as C:\\Program Files\\BackMaster.");

        if (m.Files is null || m.Files.Count == 0) errors.Add("files must list at least one file.");
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in m.Files)
            {
                if (!IsSafeRelativePath(f.Path)) errors.Add($"file path '{f.Path}' must be relative and stay inside installDir.");
                else if (!seen.Add(NormalizeRelative(f.Path))) errors.Add($"file '{f.Path}' is listed twice.");
                if (!IsSha256(f.Sha256)) errors.Add($"file '{f.Path}' has an invalid sha256.");
                if (f.Size < 0) errors.Add($"file '{f.Path}' has a negative size.");
                if (!Enum.IsDefined(f.Role)) errors.Add($"file '{f.Path}' has an unknown role.");
            }
        }

        foreach (var s in m.Services ?? Array.Empty<ServiceSpec>())
        {
            if (!ServiceNameRegex().IsMatch(s.Name ?? "")) errors.Add($"service name '{s.Name}' is invalid.");
            if (!IsSafeRelativePath(s.BinaryPath)) errors.Add($"service '{s.Name}' binaryPath must be relative to installDir.");
            if (!Enum.IsDefined(s.StartMode)) errors.Add($"service '{s.Name}' has an unknown startMode.");
        }

        foreach (var t in m.Tasks ?? Array.Empty<ScheduledTaskSpec>())
            if (!TaskNameRegex().IsMatch(t.Name ?? "")) errors.Add($"task name '{t.Name}' is invalid.");

        foreach (var r in m.Registry ?? Array.Empty<RegistryValueSpec>())
        {
            if (r.Key is null || !r.Key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) || r.Key.Contains(".."))
                errors.Add($"registry key '{r.Key}' must start with HKLM\\.");
            if (string.IsNullOrWhiteSpace(r.Name)) errors.Add($"registry value under '{r.Key}' has no name.");
            if (r.Value is null) errors.Add($"registry value '{r.Key}\\{r.Name}' has no expected value.");
        }

        var inst = m.Installer;
        if (inst is null) errors.Add("installer section is missing.");
        else
        {
            if (!ProductCodeRegex().IsMatch(inst.ProductCode ?? "")) errors.Add($"installer productCode '{inst.ProductCode}' must be a GUID in braces.");
            if (!IsAbsoluteWindowsPath(inst.CachedPath) || !inst.CachedPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                errors.Add($"installer cachedPath '{inst.CachedPath}' must be an absolute path to an .msi file.");
            if (!IsSha256(inst.Sha256)) errors.Add("installer sha256 is invalid.");
            foreach (var src in inst.FallbackSources ?? Array.Empty<string>())
                if (!(IsAbsoluteWindowsPath(src) || src.StartsWith(@"\\", StringComparison.Ordinal)) || !src.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"fallback source '{src}' must be a local or \\\\server\\share path to an .msi file.");
        }

        if (errors.Count > 0)
            throw new InvalidDataException("The install manifest is invalid:\n  - " + string.Join("\n  - ", errors));
    }

    public static string NormalizeRelative(string path) => path.Replace('/', '\\').TrimStart('\\');

    public static bool IsSha256(string? s) => s is not null && Sha256Regex().IsMatch(s);

    private static bool IsAbsoluteWindowsPath(string? p) =>
        p is not null && AbsolutePathRegex().IsMatch(p) && !p.Split('\\', '/').Contains("..");

    private static bool IsSafeRelativePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return false;
        if (p.StartsWith('\\') || p.StartsWith('/') || p.Contains(':')) return false;
        return p.Split('\\', '/').All(part => part.Length > 0 && part != "." && part != "..");
    }

    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256Regex();
    [GeneratedRegex(@"^[A-Za-z]:\\")] private static partial Regex AbsolutePathRegex();
    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")] private static partial Regex ProductCodeRegex();
    [GeneratedRegex(@"^[A-Za-z0-9_.-]{1,80}$")] private static partial Regex ServiceNameRegex();
    [GeneratedRegex(@"^[\\A-Za-z0-9 _.-]{1,200}$")] private static partial Regex TaskNameRegex();
}
