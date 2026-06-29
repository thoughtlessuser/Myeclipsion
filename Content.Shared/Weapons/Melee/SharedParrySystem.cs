using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Melee;

public sealed class SharedParrySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    private static readonly SoundPathSpecifier FinisherSound = new("/Audio/Weapons/Melee/Sword/finisher.ogg")
    {
        Params = AudioParams.Default.WithVolume(5f),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<ParryAttemptEvent>(OnParryAttempt);
    }

    private void OnParryAttempt(ParryAttemptEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        if (!TryComp<ParryComponent>(user, out var parry))
            return;

        if (!_combatMode.IsInCombatMode(user))
            return;

        if (!_melee.TryGetWeapon(user, out var weaponUid, out _) || weaponUid == user)
            return;

        var curTime = _timing.CurTime;

        if (parry.IsParrying || parry.NextParryTime > curTime)
            return;

        parry.IsParrying = true;
        parry.ParrySucceeded = false;
        parry.ParryStartTime = curTime;
        parry.ParryEndTime = curTime + TimeSpan.FromSeconds(parry.ParryWindowSeconds);
        parry.NextParryTime = curTime + TimeSpan.FromSeconds(parry.ParryCooldownSeconds);
        Dirty(user, parry);

        if (_net.IsServer)
        {
            parry.ParrySoundEntity = _audio.PlayPvs(parry.SoundEmptyParry, user)?.Entity;
            RaiseNetworkEvent(new ParryVisualEvent(GetNetEntity(user), true));
        }
    }

    private bool IsFacingAttacker(EntityUid target, EntityUid attacker)
    {
        var targetPos = _transform.GetWorldPosition(target);
        var attackerPos = _transform.GetWorldPosition(attacker);
        var dirToAttacker = attackerPos - targetPos;

        if (dirToAttacker.LengthSquared() < 0.01f)
            return true;

        var targetRotation = _transform.GetWorldRotation(target);
        var facingDir = targetRotation.ToWorldVec();
        var dot = System.Numerics.Vector2.Dot(
            System.Numerics.Vector2.Normalize(facingDir),
            System.Numerics.Vector2.Normalize(dirToAttacker));

        return dot > 0.3f;
    }

    public bool TryParry(EntityUid target, EntityUid attacker, EntityUid attackerWeapon)
    {
        if (!TryComp<ParryComponent>(target, out var parry))
            return false;

        if (!parry.IsParrying)
            return false;

        var curTime = _timing.CurTime;
        if (curTime > parry.ParryEndTime)
            return false;

        var timeSinceParry = (curTime - parry.ParryStartTime).TotalSeconds;
        var isFacing = IsFacingAttacker(target, attacker);
        var isPerfect = isFacing && timeSinceParry <= parry.PerfectParryWindowSeconds;

        parry.IsParrying = false;
        parry.ParrySucceeded = true;

        if (isPerfect)
        {
            parry.CanRiposte = true;
            parry.RiposteEndTime = curTime + TimeSpan.FromSeconds(parry.RiposteWindowSeconds);
        }

        Dirty(target, parry);

        var ev = new ParrySuccessEvent(attacker, target, attackerWeapon);
        RaiseLocalEvent(target, ev);

        if (_net.IsServer)
        {
            // Stop the empty parry sound
            if (parry.ParrySoundEntity != null)
            {
                _audio.Stop(parry.ParrySoundEntity.Value);
                parry.ParrySoundEntity = null;
            }

            if (isPerfect)
            {
                _audio.PlayPvs(parry.SoundPerfectParry, target);
                _popup.PopupEntity(Loc.GetString("melee-parry-perfect"), target, target, PopupType.Large);
                _popup.PopupEntity(Loc.GetString("melee-parry-blocked"), attacker, attacker, PopupType.MediumCaution);
                _color.RaiseEffect(Color.Yellow, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
                _color.RaiseEffect(Color.Yellow, new List<EntityUid> { attacker }, Filter.Pvs(attacker, entityManager: EntityManager));

                _stun.TrySlowdown(attacker, TimeSpan.FromSeconds(1.0), true, 0f, 0f);
            }
            else
            {
                _audio.PlayPvs(parry.SoundBlock, target);
                _popup.PopupEntity(Loc.GetString("melee-block-success"), target, target, PopupType.Medium);
                _popup.PopupEntity(Loc.GetString("melee-parry-blocked"), attacker, attacker, PopupType.MediumCaution);
                _color.RaiseEffect(Color.LightBlue, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));

                if (TryComp<StaminaComponent>(target, out _))
                    _stamina.TakeStaminaDamage(target, parry.BlockStaminaDamage, visual: false, source: attacker);
            }

            if (TryComp<StaminaComponent>(attacker, out _))
                _stamina.TakeStaminaDamage(attacker, isPerfect ? 8f : 4f, visual: false, source: target);

            if (_melee.TryGetWeapon(attacker, out var weaponUid, out var weaponComp))
            {
                var staggerTime = isPerfect ? 1.8 : 1.0;
                var staggerEnd = curTime + TimeSpan.FromSeconds(staggerTime);
                if (weaponComp.NextAttack < staggerEnd)
                {
                    weaponComp.NextAttack = staggerEnd;
                    Dirty(weaponUid, weaponComp);
                }
            }
        }

        return true;
    }

    public float GetRiposteMultiplier(EntityUid user)
    {
        if (!TryComp<ParryComponent>(user, out var parry))
            return 1f;

        if (!parry.CanRiposte)
            return 1f;

        var curTime = _timing.CurTime;
        if (curTime > parry.RiposteEndTime)
        {
            parry.CanRiposte = false;
            Dirty(user, parry);
            return 1f;
        }

        parry.CanRiposte = false;
        Dirty(user, parry);

        if (_net.IsServer)
        {
            _audio.PlayPvs(parry.SoundRiposte, user);
            _popup.PopupEntity(Loc.GetString("melee-riposte-hit"), user, user, PopupType.Large);
            _color.RaiseEffect(Color.Gold, new List<EntityUid> { user }, Filter.Pvs(user, entityManager: EntityManager));

            var weaponNet = NetEntity.Invalid;
            if (_melee.TryGetWeapon(user, out var riposteWeaponUid, out _) && riposteWeaponUid != user)
                weaponNet = GetNetEntity(riposteWeaponUid);

            RaiseNetworkEvent(new RiposteVisualEvent(GetNetEntity(user), weaponNet));
        }

        return parry.RiposteDamageMultiplier;
    }

    public void TryPlayFinisher(EntityUid target, EntityUid attacker)
    {
        if (!_net.IsServer)
            return;

        if (!TryComp<MobStateComponent>(target, out _))
            return;

        if (_mobState.IsCritical(target))
        {
            _audio.PlayPvs(FinisherSound, target);
            _color.RaiseEffect(Color.DarkRed, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ParryComponent>();

        while (query.MoveNext(out var uid, out var parry))
        {
            if (parry.IsParrying && curTime > parry.ParryEndTime)
            {
                parry.IsParrying = false;
                Dirty(uid, parry);

                if (!parry.ParrySucceeded)
                {
                    if (TryComp<StaminaComponent>(uid, out _))
                    {
                        _stamina.TakeStaminaDamage(uid, parry.FailedParryStaminaDamage, visual: true, source: uid);
                        _popup.PopupEntity(Loc.GetString("melee-parry-failed"), uid, uid, PopupType.SmallCaution);
                    }
                }
            }

            if (parry.CanRiposte && curTime > parry.RiposteEndTime)
            {
                parry.CanRiposte = false;
                Dirty(uid, parry);

                if (_melee.TryGetWeapon(uid, out var weaponUid, out var weaponComp))
                {
                    var staggerEnd = curTime + TimeSpan.FromSeconds(1.2);
                    if (weaponComp.NextAttack < staggerEnd)
                    {
                        weaponComp.NextAttack = staggerEnd;
                        Dirty(weaponUid, weaponComp);
                    }
                }

                _popup.PopupEntity(Loc.GetString("melee-riposte-missed"), uid, uid, PopupType.SmallCaution);
            }
        }
    }
}
