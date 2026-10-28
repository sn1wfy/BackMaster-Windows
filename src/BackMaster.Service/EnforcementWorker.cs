using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;
using BackMaster.Platform;

namespace BackMaster.Service;

/// <summary>
/// Every few seconds: reload policy.json if it changed, decide whether a lesson is on,
/// and make the hosts file match. Re-applying each tick also repairs manual edits.
/// When the service stops (including uninstall) the block is lifted.
/// </summary>
public sealed class EnforcementWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    private readonly ServiceOptions _options;
    private readonly EnforcementState _state;
    private readonly ILogger<EnforcementWorker> _log;
    private readonly HostsFileWriter _hosts;

    private ClassroomPolicy _policy = ClassroomPolicy.Default;
    private ClassSchedule _schedule = PolicyLoader.ToSchedule(ClassroomPolicy.Default);
    private IReadOnlyList<string> _domains = PolicyLoader.BlockedDomains(ClassroomPolicy.Default);
    private DateTime _policyStamp = DateTime.MinValue;
    private string? _policyError;
    private bool? _lastEnforce;

    public EnforcementWorker(ServiceOptions options, EnforcementState state, ILogger<EnforcementWorker> log)
    {
        _options = options;
        _state = state;
        _log = log;
        _hosts = new HostsFileWriter(options.Paths.HostsFile);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PrepareDataDirectory();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunOnce(DateTimeOffset.UtcNow);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Enforcement pass failed; retrying in {Tick}.", Tick);
            }
            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            if (_hosts.Apply(Array.Empty<string>())) _log.LogInformation("Service stopping: website block lifted.");
        }
        catch (Exception e)
        {
            _log.LogError(e, "Could not lift the website block while stopping.");
        }
    }

    private void PrepareDataDirectory()
    {
        var paths = _options.Paths;
        try
        {
            if (_options.Harden) SecureFiles.EnsureDataDirectories(paths);
            else Directory.CreateDirectory(paths.PrivateDir);

            if (!File.Exists(paths.PolicyFile))
            {
                string json = File.Exists(DataPaths.ShippedDefaultPolicy)
                    ? File.ReadAllText(DataPaths.ShippedDefaultPolicy)
                    : PolicyLoader.Serialize(ClassroomPolicy.Default);
                SecureFiles.WriteAtomic(paths.PolicyFile, json);
                _log.LogInformation("Created {Policy} from the default policy.", paths.PolicyFile);
            }
            if (!File.Exists(paths.VerifierFile))
                _log.LogWarning("No unlock phrase is set. Open \"BackMaster Settings (Administrator)\" from the Start menu to set one.");
        }
        catch (Exception e)
        {
            _log.LogError(e, "Could not prepare {DataDir}; using the built-in default policy.", paths.DataDir);
        }
    }

    private void RunOnce(DateTimeOffset now)
    {
        ReloadPolicyIfChanged();
        var decision = _schedule.Evaluate(now);
        var wanted = decision.Enforce ? _domains : Array.Empty<string>();
        bool changed = _hosts.Apply(wanted);

        if (_lastEnforce != decision.Enforce)
        {
            _log.LogInformation("Enforcement {State}: {Reason} {Count} domains blocked. Next change: {Next}.",
                decision.Enforce ? "ON" : "OFF", decision.Reason, wanted.Count, decision.NextChangeUtc?.ToString("u") ?? "none");
            _lastEnforce = decision.Enforce;
        }
        else if (changed)
        {
            _log.LogWarning("The hosts file did not match the policy and was corrected.");
        }
        _state.Update(_policy, decision, wanted.Count, _policyError);
    }

    private void ReloadPolicyIfChanged()
    {
        string file = _options.Paths.PolicyFile;
        var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        if (stamp == _policyStamp) return;
        _policyStamp = stamp;

        try
        {
            var policy = PolicyStore.LoadOrDefault(_options.Paths);
            _schedule = PolicyLoader.ToSchedule(policy);
            _domains = PolicyLoader.BlockedDomains(policy);
            _policy = policy;
            _policyError = null;
            _log.LogInformation("Policy loaded: mode {Mode}, {Periods} periods in {Zone}, {Domains} domains.",
                policy.Schedule.Mode, _schedule.Windows.Count, policy.Schedule.TimeZone, _domains.Count);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Keep enforcing the last good policy rather than failing open.
            _policyError = e.Message;
            _log.LogError("policy.json could not be loaded; keeping the previous policy. {Error}", e.Message);
        }
    }
}
