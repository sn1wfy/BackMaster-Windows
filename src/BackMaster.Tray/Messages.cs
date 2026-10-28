using System.Reflection;
using BackMaster.Core.Companion;

namespace BackMaster.Tray;

internal static class Messages
{
    /// <summary>The fox's texts, embedded in the executable so a missing file cannot silence them.</summary>
    public static CompanionMessages Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("messages.bg.json")
                           ?? throw new InvalidOperationException("messages.bg.json is not embedded.");
        using var reader = new StreamReader(stream);
        return CompanionMessages.Parse(reader.ReadToEnd());
    }
}
