using System.Numerics;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Prototypes;

namespace Content.Server._Rat.SpaceEvents.Components;

[RegisterComponent, Access(typeof(AnomalyStationRule))]
public sealed partial class AnomalyStationRuleComponent : Component
{
    [DataField]
    public float MinCoord = -3000f;

    [DataField]
    public float MaxCoord = 3000f;

    [DataField(required: true)]
    public string StationMapPath;

    [DataField(required: true)]
    public LocId Announcement;

    [DataField("anomalySpawnerPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string AnomalySpawnerPrototype = "RandomAnomalySpawner";

    public Vector2 Coordinates;
}