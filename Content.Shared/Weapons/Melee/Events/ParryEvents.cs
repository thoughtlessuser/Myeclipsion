using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Melee.Events;

[Serializable, NetSerializable]
public sealed class ParryAttemptEvent : EntityEventArgs
{
    public readonly NetEntity Weapon;

    public ParryAttemptEvent(NetEntity weapon)
    {
        Weapon = weapon;
    }
}

[Serializable, NetSerializable]
public sealed class ParryVisualEvent : EntityEventArgs
{
    public NetEntity Parrier;
    public bool Success;

    public ParryVisualEvent(NetEntity parrier, bool success)
    {
        Parrier = parrier;
        Success = success;
    }
}

[Serializable, NetSerializable]
public sealed class RiposteVisualEvent : EntityEventArgs
{
    public NetEntity Attacker;
    public NetEntity Weapon;

    public RiposteVisualEvent(NetEntity attacker, NetEntity weapon)
    {
        Attacker = attacker;
        Weapon = weapon;
    }
}

public sealed class ParrySuccessEvent : EntityEventArgs
{
    public EntityUid Attacker;
    public EntityUid Parrier;
    public EntityUid AttackerWeapon;
    public bool IsPerfect;

    public ParrySuccessEvent(EntityUid attacker, EntityUid parrier, EntityUid attackerWeapon, bool isPerfect)
    {
        Attacker = attacker;
        Parrier = parrier;
        AttackerWeapon = attackerWeapon;
        IsPerfect = isPerfect;
    }
}
