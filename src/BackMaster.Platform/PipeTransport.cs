using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BackMaster.Core.Ipc;

namespace BackMaster.Platform;

/// <summary>
/// The service end of the pipe. Any signed-in user may connect (the tray runs as the
/// student); network access is denied. Each connection gets one request and one reply.
/// </summary>
public static class PipeServer
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    public static PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // The creator needs full control to create further instances of the pipe.
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>Serves requests until <paramref name="cancel"/> fires.</summary>
    public static async Task RunAsync(string pipeName, Func<string, string> handle, Action<Exception>? onError, CancellationToken cancel)
    {
        var security = BuildSecurity();
        while (!cancel.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                await server.WaitForConnectionAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                onError?.Invoke(e);
                await Task.Delay(1000, cancel).ConfigureAwait(false);
                continue;
            }
            _ = ServeOneAsync(server, handle, onError, cancel);
        }
    }

    private static async Task ServeOneAsync(NamedPipeServerStream server, Func<string, string> handle, Action<Exception>? onError, CancellationToken cancel)
    {
        await using var _ = server;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(ClientTimeout);
            using var reader = new StreamReader(server, Utf8, false, 1024, leaveOpen: true);
            string? line = await ReadLimitedLineAsync(reader, ServiceProtocol.MaxRequestChars + 1, timeout.Token).ConfigureAwait(false);
            if (line is null) return;
            // Unlock hashes for a moment; keep that off the accept loop.
            string reply = await Task.Run(() => handle(line), cancel).ConfigureAwait(false);
            byte[] bytes = Utf8.GetBytes(reply + "\n");
            await server.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            await server.FlushAsync(timeout.Token).ConfigureAwait(false);
            server.WaitForPipeDrain();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (e is IOException) onError?.Invoke(e);
        }
    }

    /// <summary>Reads one line, giving up past <paramref name="max"/> chars so a client cannot exhaust memory.</summary>
    private static async Task<string?> ReadLimitedLineAsync(StreamReader reader, int max, CancellationToken cancel)
    {
        var sb = new StringBuilder();
        var buffer = new char[1];
        while (sb.Length < max)
        {
            int n = await reader.ReadAsync(buffer.AsMemory(), cancel).ConfigureAwait(false);
            if (n == 0) return sb.Length > 0 ? sb.ToString() : null;
            if (buffer[0] == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append(buffer[0]);
        }
        return sb.ToString();
    }
}

/// <summary>The tray's end of the pipe.</summary>
public sealed partial class PipeClient
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _pipeName;
    private readonly bool _requireService;

    /// <param name="requireService">
    /// Refuse to talk to a pipe server that is not in session 0. Services run there and
    /// user programs cannot, so a student's program that grabs the pipe name first never
    /// sees the phrase. Only tests turn this off.
    /// </param>
    public PipeClient(string pipeName = ServiceProtocol.PipeName, bool requireService = true)
    {
        _pipeName = pipeName;
        _requireService = requireService;
    }

    /// <returns>The reply, or null if the service is not reachable.</returns>
    public async Task<ServiceResponse?> SendAsync(ServiceRequest request, TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            if (_requireService && !ServerIsService(pipe)) return null;

            await pipe.WriteAsync(Utf8.GetBytes(ServiceProtocol.Encode(request) + "\n"), cts.Token).ConfigureAwait(false);
            await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Utf8);
            string? line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            return line is null ? null : ServiceProtocol.Decode<ServiceResponse>(line);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task<ServiceResponse?> StatusAsync(CancellationToken cancel = default) =>
        SendAsync(new ServiceRequest(Commands.Status), TimeSpan.FromSeconds(3), cancel);

    public Task<ServiceResponse?> UnlockAsync(string phrase, CancellationToken cancel = default) =>
        SendAsync(new ServiceRequest(Commands.Unlock, phrase), TimeSpan.FromSeconds(15), cancel);

    private static bool ServerIsService(NamedPipeClientStream pipe) =>
        GetNamedPipeServerSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out uint session) && session == 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerSessionId(nint pipe, out uint serverSessionId);
}
