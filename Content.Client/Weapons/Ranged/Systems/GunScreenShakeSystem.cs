using System;
using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client.Weapons.Ranged.Systems;

/// <summary>
/// Replaces the directional camera-kick shake with a brief oscillating burst.
/// Triggers from GunShotEvent (client-predicted, immediate) and CameraKickEvent
/// (server-confirmed, covers explosions and other sources too).
/// Runs after SharedCameraRecoilSystem so our final SetOffset wins each frame.
/// </summary>
public sealed class GunScreenShakeSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IGameTiming    _timing        = default!;

    private SharedEyeSystem _eyeSystem = default!;

    private const float ShakeDuration = 0.12f;
    private const float ShakeCooldown = 0.18f; // min time before a new shake can start
    private const float FreqX = 11f;
    private const float FreqY = 8.5f;
    private const float MagnitudeScale = 0.4f; // scales CameraKickEvent recoil
    private const float MagnitudeMax = 0.08f; // world-unit cap — keeps it subtle

    private float _remaining = 0f;
    private float _elapsed = 0f;
    private float _magnitude = 0f;
    private float _phaseX = 0f;
    private float _phaseY = 0f;
    private float _cooldown = 0f; // blocks new shakes during rapid fire

    private readonly Random _rng = new();

    public override void Initialize()
    {
        base.Initialize();
        _eyeSystem = EntityManager.System<SharedEyeSystem>();

        // Run after SharedCameraRecoilSystem so our offset wins the frame
        UpdatesAfter.Add(typeof(Content.Client.Camera.CameraRecoilSystem));

        // GunShotEvent: fires during client prediction — immediate, zero latency
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);

        // CameraKickEvent: server-confirmed, also covers explosions on the same grid
        SubscribeNetworkEvent<CameraKickEvent>(OnCameraKick);
    }

    private void OnGunShot(EntityUid uid, GunComponent gun, ref GunShotEvent args)
    {
        if (!_timing.IsFirstTimePredicted) return;
        if (args.User != _playerManager.LocalEntity) return;

        // Use a moderate default magnitude for gun shots
        var magnitude = gun.CameraRecoilScalarModified * 0.09f;
        StartShake(MathF.Min(magnitude, MagnitudeMax));
    }

    private void OnCameraKick(CameraKickEvent ev)
    {
        var player = _playerManager.LocalEntity;
        if (player == null) return;
        if (GetEntity(ev.NetEntity) != player.Value) return;

        StartShake(MathF.Min(ev.Recoil.Length() * MagnitudeScale, MagnitudeMax));
    }

    private void StartShake(float magnitude)
    {
        if (magnitude <= 0f) return;
        if (_cooldown > 0f) return; // rapid fire: skip until cooldown expires

        _magnitude = magnitude;
        _remaining = ShakeDuration;
        _elapsed = 0f;
        _cooldown = ShakeCooldown;
        _phaseX = _rng.NextSingle() * MathF.Tau;
        _phaseY = _rng.NextSingle() * MathF.Tau;
    }

    public override void FrameUpdate(float frameTime)
    {
        var player = _playerManager.LocalEntity;
        if (player == null)
        {
            _remaining = 0f;
            return;
        }

        if (!TryComp<EyeComponent>(player.Value, out var eye)) return;

        if (_cooldown > 0f)
            _cooldown -= frameTime;

        if (_remaining <= 0f)
        {
            ClearShake(player.Value, eye);
            return;
        }

        _remaining -= frameTime;
        _elapsed   += frameTime;

        if (_remaining <= 0f)
        {
            _magnitude = 0f;
            ClearShake(player.Value, eye);
            return;
        }

        var decay  = _remaining / ShakeDuration;
        var ox     = MathF.Sin(_elapsed * FreqX * MathF.Tau + _phaseX) * _magnitude * decay;
        var oy     = MathF.Sin(_elapsed * FreqY * MathF.Tau + _phaseY) * _magnitude * decay;
        var offset = new Vector2(ox, oy);

        // SetOffset runs AFTER SharedCameraRecoilSystem (UpdatesAfter), so our value wins.
        _eyeSystem.SetOffset(player.Value, offset, eye);

        // Keep BaseOffset in sync for the recoil component.
        if (TryComp<CameraRecoilComponent>(player.Value, out var recoil))
            recoil.BaseOffset = offset;
    }

    private void ClearShake(EntityUid player, EyeComponent eye)
    {
        _eyeSystem.SetOffset(player, Vector2.Zero, eye);

        if (TryComp<CameraRecoilComponent>(player, out var recoil) && recoil.BaseOffset != Vector2.Zero)
            recoil.BaseOffset = Vector2.Zero;
    }
}
