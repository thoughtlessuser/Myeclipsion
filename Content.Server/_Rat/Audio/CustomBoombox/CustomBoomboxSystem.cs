using System;
using System.IO;
using Content.Shared._Rat.Audio.CustomBoombox;
using Content.Shared.Audio.Jukebox;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Server.Upload;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Rat.Audio.CustomBoombox;

public sealed class CustomBoomboxSystem : SharedCustomBoomboxSystem
{
    private const int MaxUploadBytes = 5 * 1024 * 1024;
    private static ReadOnlySpan<byte> OggMagic => "OggS"u8;

    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly NetworkResourceManager _netResources = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        _net.RegisterNetMessage<MsgCustomBoomboxUpload>(OnUploadNet);

        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxPlayingMessage>(OnPlay);
        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxPauseMessage>(OnPause);
        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxStopMessage>(OnStop);
        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxSetTimeMessage>(OnSetTime);
        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxSetVolumeMessage>(OnSetVolume);
        SubscribeLocalEvent<CustomBoomboxComponent, CustomBoomboxClearMessage>(OnClear);
        SubscribeLocalEvent<CustomBoomboxComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CustomBoomboxComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, CustomBoomboxComponent component, ComponentStartup args)
    {
        _appearance.SetData(uid, JukeboxVisuals.VisualState, JukeboxVisualState.On);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<CustomBoomboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Selecting)
                continue;

            comp.SelectAccumulator += frameTime;
            if (comp.SelectAccumulator < 0.5f)
                continue;

            comp.SelectAccumulator = 0f;
            comp.Selecting = false;
            TryUpdateVisualState(uid, comp);

            if (comp.AutoPlayAfterSelect && comp.SelectedTrackResourcePath is { } autoPath)
            {
                comp.AutoPlayAfterSelect = false;
                comp.AudioStream = Audio.PlayPvs(
                    new SoundPathSpecifier(autoPath), uid,
                    AudioParams.Default.WithMaxDistance(10f).WithVolume(GetVolumeDb(comp.Volume)))?.Entity;
                Dirty(uid, comp);
            }
        }
    }

    private void OnShutdown(EntityUid uid, CustomBoomboxComponent component, ComponentShutdown args)
    {
        component.AudioStream = Audio.Stop(component.AudioStream);
    }

    private void OnUploadNet(MsgCustomBoomboxUpload msg)
    {
        if (!_playerManager.TryGetSessionByChannel(msg.MsgChannel, out var session) ||
            session.AttachedEntity is not { } user)
        {
            return;
        }

        var uid = GetEntity(msg.Boombox);
        if (!TryComp<CustomBoomboxComponent>(uid, out var component))
            return;

        if (!_interaction.InRangeUnobstructed(user, uid))
            return;

        if (msg.Data.Length == 0 || msg.Data.Length > MaxUploadBytes)
            return;

        if (msg.Data.Length < OggMagic.Length || !msg.Data.AsSpan().StartsWith(OggMagic))
            return;

        var baseName = Path.GetFileName(msg.FileName);
        if (string.IsNullOrEmpty(baseName) || !baseName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return;

        if (baseName.Length > 80)
            baseName = baseName[..80];

        var wasPlaying = Audio.IsPlaying(component.AudioStream);
        component.AudioStream = Audio.Stop(component.AudioStream);

        component.UploadRevision++;
        var rel = new ResPath($"custom_boombox/b{uid.Id}_{component.UploadRevision}.ogg").ToRelativePath();
        _netResources.InjectNetworkedResource(rel, msg.Data);

        var fullPath = $"/Uploaded/custom_boombox/b{uid.Id}_{component.UploadRevision}.ogg";
        component.SelectedTrackResourcePath = fullPath;
        component.SelectedTrackDisplayName = baseName;
        component.AutoPlayAfterSelect = wasPlaying;

        DirectSetVisualState(uid, JukeboxVisualState.Select);
        component.Selecting = true;
        component.SelectAccumulator = 0f;

        Dirty(uid, component);
    }

    private void OnPlay(EntityUid uid, CustomBoomboxComponent component, ref CustomBoomboxPlayingMessage args)
    {
        if (Exists(component.AudioStream))
        {
            Audio.SetState(component.AudioStream, AudioState.Playing);
            return;
        }

        component.AudioStream = Audio.Stop(component.AudioStream);

        if (component.SelectedTrackResourcePath is not { } pathStr)
            return;

        component.AudioStream = Audio.PlayPvs(
            new SoundPathSpecifier(pathStr), uid,
            AudioParams.Default.WithMaxDistance(10f).WithVolume(GetVolumeDb(component.Volume)))?.Entity;
        Dirty(uid, component);
    }

    private void OnPause(Entity<CustomBoomboxComponent> ent, ref CustomBoomboxPauseMessage args)
    {
        Audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
    }

    private static float GetVolumeDb(float volumePercent)
        => -30f + volumePercent * 0.3f; // MapToRange

    private void OnSetVolume(EntityUid uid, CustomBoomboxComponent component, CustomBoomboxSetVolumeMessage args)
    {
        component.Volume = Math.Clamp(args.Volume, 0f, 100f);
        Audio.SetVolume(component.AudioStream, GetVolumeDb(component.Volume));
        Dirty(uid, component);
    }

    private void OnClear(EntityUid uid, CustomBoomboxComponent component, CustomBoomboxClearMessage args)
    {
        component.AudioStream = Audio.Stop(component.AudioStream);
        component.SelectedTrackResourcePath = null;
        component.SelectedTrackDisplayName = null;
        component.AutoPlayAfterSelect = false;
        TryUpdateVisualState(uid, component);
        Dirty(uid, component);
    }

    private void OnSetTime(EntityUid uid, CustomBoomboxComponent component, CustomBoomboxSetTimeMessage args)
    {
        if (TryComp(args.Actor, out ActorComponent? actorComp))
        {
            var offset = actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f;
            Audio.SetPlaybackPosition(component.AudioStream, args.SongTime + offset);
        }
    }

    private void OnStop(Entity<CustomBoomboxComponent> entity, ref CustomBoomboxStopMessage args)
    {
        Audio.SetState(entity.Comp.AudioStream, AudioState.Stopped);
        Dirty(entity);
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearance.SetData(uid, JukeboxVisuals.VisualState, state);
    }

    private void TryUpdateVisualState(EntityUid uid, CustomBoomboxComponent? comp = null)
    {
        if (!Resolve(uid, ref comp))
            return;

        _appearance.SetData(uid, JukeboxVisuals.VisualState, JukeboxVisualState.On);
    }
}
