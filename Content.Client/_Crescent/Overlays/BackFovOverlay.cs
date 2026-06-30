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
using Robust.Shared.Prototypes;

namespace Content.Client._Crescent.Overlays;

/// <summary>
/// Desaturates (greyscale) the area behind the player using a screen-texture shader.
/// The transition has a soft ±7° edge so it never looks like a hard cut.
/// </summary>
public sealed class BackFovOverlay : Overlay
{
    [Dependency] private readonly IPlayerManager    _playerManager = default!;
    [Dependency] private readonly IEntityManager    _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager  = default!;

    public override OverlaySpace Space              => OverlaySpace.WorldSpace;
    public override bool         RequestScreenTexture => true;

    // 132° half-FOV → 264° visible, 96° back zone
    internal const  double HalfFovRad = 132.0 * Math.PI / 180.0;
    internal static readonly float HalfFovCos = (float) Math.Cos(HalfFovRad);

    private readonly ShaderInstance        _shader;
    private readonly SharedTransformSystem _xformSys;

    public BackFovOverlay()
    {
        IoCManager.InjectDependencies(this);
        _xformSys = _entityManager.System<SharedTransformSystem>();
        _shader   = _protoManager.Index<ShaderPrototype>("BackFov").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eye))
            return false;
        return args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null) return;

        var player = _playerManager.LocalEntity;
        if (player == null) return;

        if (!_entityManager.TryGetComponent<TransformComponent>(player.Value, out var xform))
            return;

        if (xform.MapID != args.MapId) return;

        var worldPos  = _xformSys.GetWorldPosition(xform);
        var worldRot  = _xformSys.GetWorldRotation(xform).Theta;
        // facingDir: same Y convention as FRAGCOORD (Y+ = up), no flip needed
        var facingDir = new Angle(worldRot).ToWorldVec();

        // WorldToLocal: Y=0 at top (screen-down). FRAGCOORD: Y=0 at bottom (OpenGL). Flip Y.
        var viewportLocal  = args.Viewport.WorldToLocal(worldPos);
        var playerPixelPos = new Vector2(viewportLocal.X, args.ViewportBounds.Height - viewportLocal.Y);

        _shader.SetParameter("SCREEN_TEXTURE",  ScreenTexture);
        _shader.SetParameter("playerPixelPos",  playerPixelPos);
        _shader.SetParameter("facingDir",        facingDir);
        _shader.SetParameter("halfFovCos",       HalfFovCos);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}

/// <summary>
/// Manages the back-FOV overlay and smoothly fades mob entity sprites
/// as they enter the back zone. Fade starts at the visual boundary (130°),
/// entities fully hidden at 150°.
/// </summary>
public sealed class BackFovSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayMan    = default!;
    [Dependency] private readonly IPlayerManager  _playerManager = default!;

    private SharedTransformSystem           _xformSys    = default!;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<SpriteComponent>    _spriteQuery;

    // entity → current rendered alpha (1 = fully visible, 0 = fully hidden)
    private readonly Dictionary<EntityUid, float> _smoothAlphas = new();
    private readonly HashSet<EntityUid>            _seenThisFrame = new();
    private readonly List<EntityUid>               _toClean       = new();

    private const float LookupRadius = 30f;

    private const float FadeNearCos = -0.669f; // cos(132°) — fully visible
    private const float FadeFarCos  = -0.866f; // cos(150°) — fully hidden

    // alpha units per second; asymmetric: hide quickly, reveal slowly for a dramatic feel
    private const float HideSpeed   = 5.0f;  // ~0.2 s to fully hide
    private const float RevealSpeed = 2.5f;  // ~0.4 s to fully reveal

    public override void Initialize()
    {
        base.Initialize();
        _xformSys    = EntityManager.System<SharedTransformSystem>();
        _xformQuery  = GetEntityQuery<TransformComponent>();
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
        if (localPlayer == null) { RestoreAll(); return; }

        if (!_xformQuery.TryGetComponent(localPlayer.Value, out var playerXform))
        {
            RestoreAll();
            return;
        }

        var playerPos = _xformSys.GetWorldPosition(playerXform);
        var playerRot = _xformSys.GetWorldRotation(playerXform).Theta;
        var mapId     = playerXform.MapID;
        var facingVec = new Angle(playerRot).ToWorldVec();

        _seenThisFrame.Clear();

        var enumerator = EntityManager.EntityQueryEnumerator<MobStateComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            if (uid == localPlayer.Value) continue;
            if (xform.MapID != mapId) continue;

            var entityPos = _xformSys.GetWorldPosition(xform);
            var diff      = entityPos - playerPos;
            var distSq    = diff.LengthSquared();

            if (distSq > LookupRadius * LookupRadius || distSq < 0.01f)
            {
                if (_smoothAlphas.ContainsKey(uid))
                    RestoreEntity(uid, sprite);
                continue;
            }

            var dist      = MathF.Sqrt(distSq);
            var dir       = diff / dist;
            var dot       = facingVec.X * dir.X + facingVec.Y * dir.Y;
            var targetAlpha = MathF.Max(0f, MathF.Min(1f, (dot - FadeFarCos) / (FadeNearCos - FadeFarCos)));

            // Retrieve or initialise the smooth alpha for this entity
            if (!_smoothAlphas.TryGetValue(uid, out var current))
                current = 1.0f;

            // Move current toward target — different speed for hiding vs revealing
            var speed = targetAlpha > current ? RevealSpeed : HideSpeed;
            current = targetAlpha > current
                ? MathF.Min(current + speed * frameTime, targetAlpha)
                : MathF.Max(current - speed * frameTime, targetAlpha);

            if (current >= 0.999f)
            {
                // Fully visible — stop tracking
                if (_smoothAlphas.ContainsKey(uid))
                    RestoreEntity(uid, sprite);
            }
            else
            {
                _smoothAlphas[uid] = current;
                sprite.Visible     = current > 0.01f;
                sprite.Color       = Color.White.WithAlpha(current);
                _seenThisFrame.Add(uid);
            }
        }

        // Clean up entries that left the query (deleted, teleported, etc.)
        _toClean.Clear();
        foreach (var uid in _smoothAlphas.Keys)
        {
            if (!_seenThisFrame.Contains(uid))
                _toClean.Add(uid);
        }
        foreach (var uid in _toClean)
        {
            if (_spriteQuery.TryGetComponent(uid, out var sprite))
                RestoreEntity(uid, sprite);
            else
                _smoothAlphas.Remove(uid);
        }
    }

    private void RestoreEntity(EntityUid uid, SpriteComponent sprite)
    {
        sprite.Visible = true;
        sprite.Color   = Color.White;
        _smoothAlphas.Remove(uid);
    }

    private void RestoreAll()
    {
        foreach (var uid in _smoothAlphas.Keys)
        {
            if (_spriteQuery.TryGetComponent(uid, out var sprite))
            {
                sprite.Visible = true;
                sprite.Color   = Color.White;
            }
        }
        _smoothAlphas.Clear();
    }
}
