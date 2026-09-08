namespace PowerFlow.Windows.Games;

public sealed record GameHeuristicSample(int ProcessId, bool IsForegroundFullscreen, string ExecutablePath, DateTimeOffset At);

public sealed class GameHeuristic
{
    private int? _candidatePid;
    private int _consecutive;

    public bool Observe(GameHeuristicSample sample)
    {
        if (!sample.IsForegroundFullscreen)
        {
            _candidatePid = null;
            _consecutive = 0;
            return false;
        }

        if (_candidatePid == sample.ProcessId) _consecutive++;
        else { _candidatePid = sample.ProcessId; _consecutive = 1; }
        return _consecutive >= 2;
    }

    public void Reset()
    {
        _candidatePid = null;
        _consecutive = 0;
    }
}
