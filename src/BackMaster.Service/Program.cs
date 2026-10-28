using BackMaster.Core.Ipc;
using BackMaster.Core.Security;
using BackMaster.Platform;
using BackMaster.Service;

// Installed as the "BackMaster" Windows service running as LocalSystem. It can also be
// run from an elevated console for testing; these switches only exist on the command
// line, which only an administrator can set for the installed service:
//   --data-dir <dir>   use another folder instead of %ProgramData%\BackMaster
//   --hosts <file>     edit another file instead of the real hosts file
//   --pipe <name>      listen on another pipe name
//   --no-harden        do not change folder permissions (for non-elevated test runs)
var options = ServiceOptions.FromArgs(args);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "BackMaster");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<EnforcementState>();
builder.Services.AddSingleton(new AttemptThrottle());
builder.Services.AddHostedService<EnforcementWorker>();
builder.Services.AddHostedService<PipeWorker>();

builder.Build().Run();

namespace BackMaster.Service
{
    public sealed record ServiceOptions(DataPaths Paths, string PipeName, bool Harden)
    {
        public static ServiceOptions FromArgs(string[] args)
        {
            var paths = DataPaths.Default;
            string pipe = ServiceProtocol.PipeName;
            bool harden = true;
            for (int i = 0; i < args.Length; i++)
            {
                string? Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
                switch (args[i])
                {
                    case "--data-dir": paths = paths with { DataDir = Path.GetFullPath(Next()!) }; break;
                    case "--hosts": paths = paths with { HostsFile = Path.GetFullPath(Next()!) }; break;
                    case "--pipe": pipe = Next()!; break;
                    case "--no-harden": harden = false; break;
                }
            }
            return new ServiceOptions(paths, pipe, harden);
        }
    }
}
