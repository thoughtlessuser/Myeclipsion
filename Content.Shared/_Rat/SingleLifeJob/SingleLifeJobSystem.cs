namespace Content.Shared._Rat.SingleLifeJob;  

public abstract class SingleLifeJobTrackerSystem : EntitySystem  
{  
    public abstract bool HasPlayedThisRound(string jobId);  
}