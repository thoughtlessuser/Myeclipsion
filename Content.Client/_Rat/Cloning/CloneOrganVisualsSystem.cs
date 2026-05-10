using Content.Shared._Rat.Cloning;
using Robust.Client.GameObjects;

namespace Content.Client._Rat.Cloning;

public sealed class CloneOrganVisualsSystem : EntitySystem
{
    private const string ShaderName = "CloneOrganOverlay";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CloneOrganComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CloneOrganComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CloneOrganComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnStartup(EntityUid uid, CloneOrganComponent component, ComponentStartup args)
    {
        ApplyShader(uid);
    }

    private void OnShutdown(EntityUid uid, CloneOrganComponent component, ComponentShutdown args)
    {
        if (Terminating(uid) || !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        var idx = 0;
        foreach (var _ in sprite.AllLayers)
        {
            sprite.LayerSetShader(idx, null, null);
            idx++;
        }
    }

    private void OnAppearanceChange(EntityUid uid, CloneOrganComponent component, ref AppearanceChangeEvent args)
    {
        ApplyShader(uid, args.Sprite);
    }

    private void ApplyShader(EntityUid uid, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        var idx = 0;
        foreach (var _ in sprite.AllLayers)
        {
            sprite.LayerSetShader(idx, ShaderName);
            idx++;
        }
    }
}
