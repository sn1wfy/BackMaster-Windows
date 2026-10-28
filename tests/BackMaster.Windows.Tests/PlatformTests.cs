using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BackMaster.Core.Filtering;
using BackMaster.Core.Ipc;
using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;
using BackMaster.Core.Security;
using BackMaster.Platform;

namespace BackMaster.Windows.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("BackMasterTests-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class HostsFileWriterTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Blocks_and_unblocks_while_preserving_non_ascii_bytes()
    {
        string hosts = Path.Combine(_dir.Path, "hosts");
        // 0xE8 is "и" in Windows-1251; the writer must not re-encode it.
        byte[] original = Encoding.ASCII.GetBytes("# comment ").Concat(new byte[] { 0xE8 }).Concat(Encoding.ASCII.GetBytes("\r\n127.0.0.1 a.local\r\n")).ToArray();
        File.WriteAllBytes(hosts, original);
        var writer = new HostsFileWriter(hosts);

        Assert.True(writer.Apply(new[] { "chatgpt.com" }));
        Assert.True(writer.IsBlocking());
        Assert.Contains("0.0.0.0 www.chatgpt.com", File.ReadAllText(hosts));
        Assert.False(writer.Apply(new[] { "chatgpt.com" })); // already correct: no write

        Assert.True(writer.Apply(Array.Empty<string>()));
        Assert.Equal(original, File.ReadAllBytes(hosts));
        Assert.False(writer.IsBlocking());
    }

    [Fact]
    public void A_read_only_hosts_file_is_still_updated()
    {
        string hosts = Path.Combine(_dir.Path, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 a.local\r\n");
        File.SetAttributes(hosts, FileAttributes.ReadOnly);
        Assert.True(new HostsFileWriter(hosts).Apply(new[] { "poki.com" }));
        Assert.Contains(HostsSection.BeginMarker, File.ReadAllText(hosts));
    }
}

public class PolicyStoreTests : IDisposable
{
    private readonly TempDir _dir = new();
    private DataPaths Paths => new(_dir.Path, Path.Combine(_dir.Path, "hosts"));

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void An_untampered_payload_is_applied()
    {
        var change = new PolicyChange(ClassroomPolicy.Default with { Access = new AccessSection("Ctrl+Alt+F8") },
            PhraseVerifier.Create("учителска фраза", 100_000));
        string payload = PolicyStore.Encode(change);

        var decoded = PolicyStore.DecodeVerified(payload, PolicyStore.Hash(payload));
        PolicyStore.Apply(Paths, decoded, harden: false);

        Assert.Equal("Ctrl+Alt+F8", PolicyStore.LoadOrDefault(Paths).Access.Hotkey);
        Assert.True(PhraseVerifier.Verify("учителска фраза", PolicyStore.ReadVerifier(Paths)));
        Assert.DoesNotContain("учителска", File.ReadAllText(Paths.VerifierFile));
        Assert.DoesNotContain("pbkdf2", File.ReadAllText(Paths.PolicyFile));
    }

    [Fact]
    public void A_payload_swapped_after_approval_is_refused()
    {
        string approved = PolicyStore.Encode(new PolicyChange(ClassroomPolicy.Default, null));
        string swapped = PolicyStore.Encode(new PolicyChange(ClassroomPolicy.Default, PhraseVerifier.Create("студентска фраза", 100_000)));
        Assert.Throws<InvalidDataException>(() => PolicyStore.DecodeVerified(swapped, PolicyStore.Hash(approved)));
    }

    [Fact]
    public void An_invalid_policy_is_not_written()
    {
        var bad = ClassroomPolicy.Default with { Access = new AccessSection("B") };
        Assert.Throws<InvalidDataException>(() => PolicyStore.Apply(Paths, new PolicyChange(bad, null), harden: false));
        Assert.False(File.Exists(Paths.PolicyFile));
    }

    [Fact]
    public void Keeping_the_phrase_leaves_the_verifier_alone()
    {
        string verifier = PhraseVerifier.Create("първа фраза!", 100_000);
        PolicyStore.Apply(Paths, new PolicyChange(ClassroomPolicy.Default, verifier), harden: false);
        PolicyStore.Apply(Paths, new PolicyChange(ClassroomPolicy.Default, null), harden: false);
        Assert.Equal(verifier, PolicyStore.ReadVerifier(Paths));
    }

    [Fact]
    public void Without_files_the_shipped_default_is_used()
    {
        var p = PolicyStore.LoadOrDefault(Paths);
        Assert.Equal(EnforcementMode.Scheduled, p.Schedule.Mode);
        Assert.Equal(2, p.Schedule.Periods.Count);
        Assert.Null(PolicyStore.ReadVerifier(Paths));
    }
}

public class SecureFilesTests
{
    [Fact]
    public void Data_folder_acl_is_protected_and_users_can_only_read()
    {
        var acl = SecureFiles.BuildSecurity(usersCanRead: true);
        Assert.True(acl.AreAccessRulesProtected);
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        var users = Assert.Single(rules, r => r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)));
        Assert.Equal(FileSystemRights.ReadAndExecute, users.FileSystemRights & ~FileSystemRights.Synchronize);
        Assert.Contains(rules, r => r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)) && r.FileSystemRights == FileSystemRights.FullControl);
    }

    [Fact]
    public void Private_folder_acl_excludes_users()
    {
        var rules = SecureFiles.BuildSecurity(usersCanRead: false).GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        Assert.All(rules, r => Assert.True(
            r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)) ||
            r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null))));
    }
}

public class PipeTests
{
    [Fact]
    public async Task Status_and_unlock_round_trip_over_a_real_pipe()
    {
        string pipe = "BackMaster.Test." + Guid.NewGuid().ToString("N");
        var status = new ServiceStatus(true, EnforcementMode.Scheduled, DateTimeOffset.UtcNow, "Europe/Sofia", 52, "test");
        var handler = new RequestHandler(() => status, () => PhraseVerifier.Create("правилна фраза", 100_000), new AttemptThrottle());
        using var stop = new CancellationTokenSource();
        var server = PipeServer.RunAsync(pipe, handler.HandleLine, null, stop.Token);

        var client = new PipeClient(pipe, requireService: false);
        Assert.Equal(status, (await client.StatusAsync())!.Status);
        Assert.Equal(UnlockResult.Accepted, (await client.UnlockAsync("правилна фраза"))!.Unlock);
        Assert.Equal(UnlockResult.Rejected, (await client.UnlockAsync("грешна фраза"))!.Unlock);

        // Several clients at once.
        var many = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.StatusAsync()));
        Assert.All(many, r => Assert.True(r!.Ok));

        stop.Cancel();
        await server;
    }

    [Fact]
    public async Task A_pipe_not_owned_by_a_service_is_not_trusted()
    {
        // This test process is in the user's session, not session 0, exactly like a
        // student's program trying to impersonate the service.
        string pipe = "BackMaster.Test." + Guid.NewGuid().ToString("N");
        using var stop = new CancellationTokenSource();
        var server = PipeServer.RunAsync(pipe, _ => ServiceProtocol.Encode(new ServiceResponse(true, Unlock: UnlockResult.Accepted)), null, stop.Token);

        Assert.Null(await new PipeClient(pipe, requireService: true).UnlockAsync("phrase sent to an impostor"));

        stop.Cancel();
        await server;
    }

    [Fact]
    public async Task No_server_means_null_not_an_exception()
    {
        Assert.Null(await new PipeClient("BackMaster.Test.nobody", requireService: false).SendAsync(new ServiceRequest(Commands.Status), TimeSpan.FromMilliseconds(300)));
    }
}
