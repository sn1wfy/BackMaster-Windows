using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using BackMaster.Core.Filtering;

namespace BackMaster.Service;

/// <summary>
/// Notices when a browser tries to reach a blocked site, so the tray can show the fox.
/// The hosts file makes the lookup fail silently; the DNS Client's operational log still
/// records the lookup (event 3006, "query is called", QueryName first) together with the
/// process that made it. Only lookups by browsers during a lesson, for names that are
/// actually blocked, become attempts. Nothing is stored beyond the tray's short feed.
/// </summary>
public sealed class DnsWatcher : BackgroundService
{
    public const string LogName = "Microsoft-Windows-DNS-Client/Operational";
    public const string EnabledMarker = "dnslog-enabled-by-backmaster";
    private const string Query = "*[System[(EventID=3006)]]";

    // Lookups by other programs (Windows widgets, Copilot app, updaters) do not mean a
    // student is trying to open the site, so they never trigger the fox.
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "chrome", "firefox", "brave", "opera", "opera_gx", "vivaldi", "iexplore", "arc", "yandex", "browser",
    };

    private readonly ServiceOptions _options;
    private readonly EnforcementState _state;
    private readonly AttemptFeed _feed;
    private readonly ILogger<DnsWatcher> _log;
    private int _loggedProcesses;

    public DnsWatcher(ServiceOptions options, EnforcementState state, AttemptFeed feed, ILogger<DnsWatcher> log)
    {
        _options = options;
        _state = state;
        _feed = feed;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EventLogWatcher? watcher = null;
        try
        {
            EnsureLogEnabled();
            watcher = new EventLogWatcher(new EventLogQuery(LogName, PathType.LogName, Query));
            watcher.EventRecordWritten += OnRecord;
            watcher.Enabled = true;
            _log.LogInformation("Watching DNS lookups for blocked sites.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException)
        {
            // Blocking still works without this; only the fox's reaction is lost.
            _log.LogWarning("Cannot watch DNS lookups, so the fox will not react to blocked sites: {Error}", e.Message);
        }
        finally
        {
            if (watcher is not null)
            {
                watcher.Enabled = false;
                watcher.Dispose();
            }
        }
    }

    /// <summary>The log is off by default. Turn it on and leave a marker so removal can turn it off again.</summary>
    private void EnsureLogEnabled()
    {
        using var config = new EventLogConfiguration(LogName);
        if (config.IsEnabled) return;
        config.IsEnabled = true;
        config.SaveChanges();
        try
        {
            File.WriteAllText(Path.Combine(_options.Paths.DataDir, EnabledMarker), DateTimeOffset.UtcNow.ToString("u"));
        }
        catch (IOException)
        {
        }
        _log.LogInformation("Enabled the {Log} event log.", LogName);
    }

    private void OnRecord(object? sender, EventRecordWrittenEventArgs e)
    {
        using var record = e.EventRecord;
        if (record is null) return;
        try
        {
            var matcher = _state.ActiveBlocks;
            if (matcher.Count == 0) return;
            if (record.Properties.Count == 0 || matcher.Match(record.Properties[0].Value as string) is not { } site) return;

            string process = ProcessName(record.ProcessId);
            if (_loggedProcesses < 20)
            {
                // Early lookups are logged with their process, to confirm on a new PC which programs show up.
                _loggedProcesses++;
                _log.LogInformation("Blocked lookup of {Domain} by {Process}.", site.Domain, process);
            }
            if (!Browsers.Contains(process)) return;

            if (_feed.Record(site, DateTimeOffset.UtcNow) is { } attempt)
                _log.LogInformation("Student tried to open {Domain}; the fox will explain.", attempt.Domain);
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException)
        {
            _log.LogDebug("Skipped a DNS event: {Error}", ex.Message);
        }
    }

    private static string ProcessName(int? pid)
    {
        if (pid is null) return "?";
        try
        {
            using var p = Process.GetProcessById(pid.Value);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return "?"; // already exited
        }
    }
}
