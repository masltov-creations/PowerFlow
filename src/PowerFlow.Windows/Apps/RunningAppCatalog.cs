using System.Diagnostics;

namespace PowerFlow.Windows.Apps;

public sealed class RunningAppCatalog
{
    public IReadOnlyList<RunningAppOption> List()
    {
        var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        var candidates = new List<RunningAppCandidate>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == current.Id || process.SessionId != session || process.MainWindowHandle == IntPtr.Zero) continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var name = !string.IsNullOrWhiteSpace(process.MainWindowTitle)
                        ? process.MainWindowTitle.Trim()
                        : Path.GetFileNameWithoutExtension(path);
                    candidates.Add(new RunningAppCandidate(process.Id, name, path));
                }
                catch
                {
                    // Protected/system processes are not picker candidates.
                }
            }
        }
        return RunningAppProjection.Project(candidates);
    }
}
