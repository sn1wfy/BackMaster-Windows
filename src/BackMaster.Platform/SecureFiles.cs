using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace BackMaster.Platform;

/// <summary>
/// Creates BackMaster's data folders with explicit permissions. %ProgramData% lets
/// standard users create files in subfolders by default, so inheritance is cut and
/// replaced with a fixed list: SYSTEM and Administrators full control, Users read (or
/// nothing, for the private folder holding the phrase verifier).
/// </summary>
public static class SecureFiles
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    public static void EnsureDataDirectories(DataPaths paths)
    {
        Harden(paths.DataDir, usersCanRead: true);
        Harden(paths.PrivateDir, usersCanRead: false);
    }

    public static DirectorySecurity BuildSecurity(bool usersCanRead)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersCanRead)
            security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    public static void Harden(string directory, bool usersCanRead)
    {
        var dir = new DirectoryInfo(directory);
        if (!dir.Exists) dir.Create();
        var security = BuildSecurity(usersCanRead);
        // Taking ownership away from whoever created the folder needs elevation; the
        // service and the elevated helper have it.
        if (Elevation.IsElevated) security.SetOwner(Administrators);
        dir.SetAccessControl(security);
    }

    /// <summary>
    /// Writes through a temporary file in the same folder and swaps it in, so a crash or
    /// power cut never leaves a half-written policy. The new file inherits the folder's ACL.
    /// </summary>
    public static void WriteAtomic(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
