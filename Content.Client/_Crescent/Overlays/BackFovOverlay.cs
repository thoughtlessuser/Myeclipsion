using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client._Crescent.Overlays;

public sealed class BackFovOverlay : Overlay
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    // 130° each side = 260° forward FOV, 100° back dark zone
    internal const double HalfFovRad = 130.0 * Math.PI / 180.0;

    private const float Alpha = 0.45f;
    private const float InnerRadius = 1.1f;
    private const float OuterRadius = 65f;
    private const int Segments = 72;

    private readonly SharedTransformSystem _xformSys;

    public BackFovOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entityManager.System<SharedTransformSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return false;
        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var player = _playerManager.LocalEntity;
        if (player == null)
            return;

        if (!_entityManager.TryGetComponent<TransformComponent>(player.Value, out var xform))
            return;

        if (xform.MapID != args.MapId)
            return;

        var worldPos = _xformSys.GetWorldPosition(xform);
        var worldRotRad = _xformSys.GetWorldRotation(xform).Theta;

        var backStart = worldRotRad + HalfFovRad;
        var backArc = 2.0 * Math.PI - 2.0 * HalfFovRad;

        DrawDonutArc(args.WorldHandle, worldPos, backStart, backArc, Segments, Alpha);
    }

    private static void DrawDonutArc(DrawingHandleWorld handle, Vector2 origin,
        double startRad, double arcRad, int segs, float alpha)
    {
        var triangles = new Vector2[segs * 6];

        for (var i = 0; i < segs; i++)
        {
            var t0 = i / (double) segs;
            var t1 = (i + 1) / (double) segs;
            var angle0 = new Angle(startRad + arcRad * t0);
            var angle1 = new Angle(startRad + arcRad * t1);
            var dir0 = angle0.ToWorldVec();
            var dir1 = angle1.ToWorldVec();

            var innerA = origin + dir0 * InnerRadius;
            var outerA = origin + dir0 * OuterRadius;
            var innerB = origin + dir1 * InnerRadius;
            var outerB = origin + dir1 * OuterRadius;

            var idx = i * 6;
            triangles[idx]     = innerA;
            triangles[idx + 1] = outerA;
            triangles[idx + 2] = outerB;
            triangles[idx + 3] = innerA;
            triangles[idx + 4] = outerB;
            triangles[idx + 5] = innerB;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, triangles, Color.Black.WithAlpha(alpha));
    }
}

/// <summary>
/// Manages the back-FOV overlay and hides mob entity sprites (players, NPCs, robots)
/// that are in the player's back zone. Terrain tiles remain visible.
/// </summary>
public sealed class BackFovSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private SharedTransformSystem _xformSys = default!;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;

    private readonly HashSet<EntityUid> _hiddenEntities = new();
    private readonly HashSet<EntityUid> _currentBackZone = new();
    private readonly List<EntityUid> _toRestore = new();

    private const float LookupRadius = 30f;

    public override void Initialize()
    {
        base.Initialize();
        _xformSys = EntityManager.System<SharedTransformSystem>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();
        _overlayMan.AddOverlay(new BackFovOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayMan.RemoveOverlay<BackFovOverlay>();
        RestoreAll();
    }

    public override void FrameUpdate(float frameTime)
    {
        var localPlayer = _playerManager.LocalEntity;
        if (localPlayer == null)
        {
            RestoreAll();
            return;
        }

        if (!_xformQuery.TryGetComponent(localPlayer.Value, out var playerXform))
        {
            RestoreAll();
            return;
        }

        var playerPos = _xformSys.GetWorldPosition(playerXform);
        var playerRot = _xformSys.GetWorldRotation(playerXform).Theta;
        var mapId = playerXform.MapID;

        _currentBackZone.Clear();

        // Use the same vector math as the overlay to avoid coordinate convention mismatches
        var facingVec = new Angle(playerRot).ToWorldVec();
        // Entity is in back zone when dot(facing, dirToEntity) < cos(HalfFovRad)
        // cos(130°) ≈ -0.643
        var frontCos = (float) Math.Cos(BackFovOverlay.HalfFovRad);

        var enumerator = EntityManager.EntityQueryEnumerator<MobStateComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (uid == localPlayer.Value)
                continue;

            if (xform.MapID != mapId)
                continue;

            var entityPos = _xformSys.GetWorldPosition(xform);
            var diff = entityPos - playerPos;
            var distSq = diff.LengthSquared();

            if (distSq > LookupRadius * LookupRadius || distSq < 0.01f)
                continue;

            var dist = MathF.Sqrt(distSq);
            var dirToEntity = diff / dist;
            var dot = facingVec.X * dirToEntity.X + facingVec.Y * dirToEntity.Y;

            if (dot < frontCos)
                _currentBackZone.Add(uid);
        }

        foreach (var uid in _currentBackZone)
        {
            if (!_hiddenEntities.Contains(uid) && _spriteQuery.TryGetComponent(uid, out var sprite))
            {
                sprite.Visible = false;
                _hiddenEntities.Add(uid);
            }
        }

        _toRestore.Clear();
        foreach (var uid in _hiddenEntities)
        {
            if (!_currentBackZone.Contains(uid))
                _toRestore.Add(uid);
        }

        foreach (var uid in _toRestore)
        {
            if (_spriteQuery.TryGetComponent(uid, out var sprite))
                sprite.Visible = true;
            _hiddenEntities.Remove(uid);
        }
    }

    private void RestoreAll()
    {
        foreach (var uid in _hiddenEntities)
        {
            if (_spriteQuery.TryGetComponent(uid, out var sprite))
                sprite.Visible = true;
        }
        _hiddenEntities.Clear();
    }
}
