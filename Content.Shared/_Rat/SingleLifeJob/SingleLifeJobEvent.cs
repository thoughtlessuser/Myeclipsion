using Robust.Shared.Serialization;

namespace Content.Shared._Rat.SingleLifeJob;

[Serializable, NetSerializable]
public sealed class SingleLifeJobPlayedEvent : EntityEventArgs
{
    public string JobId;

    public SingleLifeJobPlayedEvent(string jobId)
    {
        JobId = jobId;
    }
}