using Robust.Shared.Prototypes;
using Content.Shared.Roles;

namespace Content.Server._Rat.LifeInsurance.Components;

/// <summary>
/// Stored on the mind entity (<see cref="Content.Shared.Mind.Components.MindComponent"/>), not on the mob/brain Uid.
/// </summary>
[RegisterComponent]
public sealed partial class LifeInsuranceComponent : Component
{
    [DataField]
    public bool IsInsured;

    [DataField]
    public int RespawnCount;

    [DataField]
    public TimeSpan? PendingRespawnAt;

    [DataField]
    public ProtoId<JobPrototype>? PendingRespawnJob;

    [DataField]
    public EntityUid? PendingRespawnStation;

    /// <summary>
    /// Spawn machine this mind should respawn at when insurance pays out. Set from the insurance console.
    /// </summary>
    [DataField]
    public EntityUid? PreferredSpawnMachine;
}
