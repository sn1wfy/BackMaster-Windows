using BackMaster.Core.Policy;

namespace BackMaster.Core.Filtering;

/// <param name="Domain">The domain as listed in the policy, e.g. "chatgpt.com" for a lookup of www.chatgpt.com.</param>
/// <param name="IsAi">Listed under AI assistants (the fox uses the ai_blocked message).</param>
public sealed record BlockedSite(string Domain, bool IsAi);

/// <summary>One attempt to reach a blocked site, as reported to the tray.</summary>
public sealed record BlockedAttempt(long Id, string Domain, bool IsAi, DateTimeOffset AtUtc);

/// <summary>
/// Maps a looked-up host name to the policy entry it belongs to. Only the names actually
/// written to the hosts file match, so the fox never claims a site is blocked when it is not.
/// </summary>
public sealed class BlockMatcher
{
    private readonly Dictionary<string, BlockedSite> _byHost = new(StringComparer.Ordinal);

    public static BlockMatcher Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    public BlockMatcher(IEnumerable<string> aiDomains, IEnumerable<string> otherDomains)
    {
        // AI entries first, so a domain listed in both lists counts as AI.
        foreach (var (list, isAi) in new[] { (aiDomains, true), (otherDomains, false) })
        {
            foreach (var raw in list)
            {
                if (DomainName.Normalize(raw) is not { } domain) continue;
                foreach (var host in HostsSection.HostNames(new[] { domain }))
                    _byHost.TryAdd(host, new BlockedSite(domain, isAi));
            }
        }
    }

    public static BlockMatcher From(ClassroomPolicy p) => new(p.Filtering.AiDomains, p.Filtering.BlockedDomains);

    public int Count => _byHost.Count;

    public BlockedSite? Match(string? queryName)
    {
        if (string.IsNullOrWhiteSpace(queryName)) return null;
        return _byHost.GetValueOrDefault(queryName.Trim().TrimEnd('.').ToLowerInvariant());
    }
}

/// <summary>
/// The recent attempts the tray has not shown yet. A browser looks a name up several times
/// per page (and again for www.), so each site is reported at most once per quiet period.
/// </summary>
public sealed class AttemptFeed
{
    private readonly object _gate = new();
    private readonly Queue<BlockedAttempt> _recent = new();
    private readonly Dictionary<string, DateTimeOffset> _lastReported = new(StringComparer.Ordinal);
    private long _nextId = 1;

    public TimeSpan QuietPeriod { get; }
    public int Capacity { get; }

    public AttemptFeed(TimeSpan? quietPeriod = null, int capacity = 32)
    {
        QuietPeriod = quietPeriod ?? TimeSpan.FromSeconds(60);
        Capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    /// <summary>The id of the newest attempt, or 0 if there has been none.</summary>
    public long Cursor
    {
        get { lock (_gate) return _nextId - 1; }
    }

    /// <returns>The new attempt, or null if the site was reported within the quiet period.</returns>
    public BlockedAttempt? Record(BlockedSite site, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastReported.TryGetValue(site.Domain, out var last) && now - last < QuietPeriod) return null;
            _lastReported[site.Domain] = now;

            var attempt = new BlockedAttempt(_nextId++, site.Domain, site.IsAi, now);
            _recent.Enqueue(attempt);
            while (_recent.Count > Capacity) _recent.Dequeue();
            return attempt;
        }
    }

    /// <summary>
    /// Attempts newer than <paramref name="cursor"/>. A cursor ahead of the feed means the
    /// service restarted, so everything it has is new.
    /// </summary>
    public IReadOnlyList<BlockedAttempt> After(long cursor)
    {
        lock (_gate)
        {
            if (cursor > _nextId - 1) cursor = 0;
            return _recent.Where(a => a.Id > cursor).ToList();
        }
    }
}
