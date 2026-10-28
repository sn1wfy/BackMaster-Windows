using BackMaster.Core.Ipc;
using BackMaster.Core.Policy;
using BackMaster.Core.Scheduling;

namespace BackMaster.Service;

/// <summary>The latest policy and decision, shared between the enforcement loop and the pipe.</summary>
public sealed class EnforcementState
{
    private readonly object _gate = new();
    private ServiceStatus _status = new(false, EnforcementMode.Scheduled, null, ClassSchedule.DefaultTimeZoneId, 0, "Starting.");

    public ServiceStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public void Update(ClassroomPolicy policy, ScheduleDecision decision, int blockedCount, string? policyError)
    {
        var status = new ServiceStatus(decision.Enforce, policy.Schedule.Mode, decision.NextChangeUtc,
            policy.Schedule.TimeZone, blockedCount, decision.Reason, policyError);
        lock (_gate) _status = status;
    }
}
