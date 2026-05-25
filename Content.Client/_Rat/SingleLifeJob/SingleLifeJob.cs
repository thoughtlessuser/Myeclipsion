using Content.Shared._Rat.SingleLifeJob;
using Content.Shared.GameTicking;

namespace Content.Client._Rat.SingleLifeJob;

public sealed class SingleLifeJobClientSystem : SingleLifeJobTrackerSystem
{
    private readonly HashSet<string> _playedJobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SingleLifeJobPlayedEvent>(OnJobPlayed);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnJobPlayed(SingleLifeJobPlayedEvent ev)
    {
        _playedJobs.Add(ev.JobId);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _playedJobs.Clear();
    }

    public override bool HasPlayedThisRound(string jobId) => _playedJobs.Contains(jobId);
}