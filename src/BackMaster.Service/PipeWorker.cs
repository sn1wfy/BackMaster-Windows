using BackMaster.Core.Filtering;
using BackMaster.Core.Ipc;
using BackMaster.Core.Security;
using BackMaster.Platform;

namespace BackMaster.Service;

/// <summary>Answers the tray: status (with blocked attempts for the fox) and unlock-phrase checks.</summary>
public sealed class PipeWorker : BackgroundService
{
    private readonly ServiceOptions _options;
    private readonly RequestHandler _handler;
    private readonly ILogger<PipeWorker> _log;

    public PipeWorker(ServiceOptions options, EnforcementState state, AttemptThrottle throttle, AttemptFeed attempts, ILogger<PipeWorker> log)
    {
        _options = options;
        _log = log;
        _handler = new RequestHandler(() => state.Status, () => PolicyStore.ReadVerifier(options.Paths), throttle, attempts: attempts);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        PipeServer.RunAsync(_options.PipeName, Handle, e => _log.LogWarning("Pipe error: {Error}", e.Message), stoppingToken);

    private string Handle(string line)
    {
        string reply = _handler.HandleLine(line);
        // Log unlock outcomes (never the phrase) so repeated guessing is visible to the admin.
        if (line.Contains("\"unlock\"", StringComparison.Ordinal))
        {
            var r = ServiceProtocol.Decode<ServiceResponse>(reply);
            if (r?.Unlock is UnlockResult.Accepted) _log.LogInformation("Settings unlocked with the phrase.");
            else _log.LogWarning("Settings unlock refused: {Result}.", r?.Unlock);
        }
        return reply;
    }
}
