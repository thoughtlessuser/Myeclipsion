using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Shared._Rat.SignalTransmitter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Rat.SignalTransmitter;

public sealed class SignalTransmitterDeviceSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundPathSpecifier ActivateSound = new("/Audio/Effects/double_beep.ogg");

    /// <summary>
    /// Tracks the global timer state. Only one transmitter timer can be active at a time.
    /// </summary>
    private bool _timerActive;
    private TimeSpan _timerEnd;
    private bool _reminderSent;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SignalTransmitterDeviceComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(EntityUid uid, SignalTransmitterDeviceComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (comp.Activated)
        {
            _popup.PopupEntity(Loc.GetString("transmitter-already-activated"), uid, args.User, PopupType.MediumCaution);
            return;
        }

        if (_timerActive)
        {
            _popup.PopupEntity(Loc.GetString("transmitter-timer-already-active"), uid, args.User, PopupType.MediumCaution);
            return;
        }

        var xform = Transform(uid);
        if (!xform.Anchored)
        {
            _popup.PopupEntity(Loc.GetString("transmitter-not-anchored"), uid, args.User, PopupType.MediumCaution);
            return;
        }

        comp.Activated = true;
        _timerActive = true;
        _timerEnd = _timing.CurTime + TimeSpan.FromMinutes(comp.TimerMinutes);
        _reminderSent = false;

        _audio.PlayPvs(ActivateSound, uid, AudioParams.Default.WithVolume(4f));

        // Send the initial global announcement
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("transmitter-activated-announcement", ("minutes", comp.TimerMinutes)),
            Loc.GetString("transmitter-sender"),
            true,
            colorOverride: Color.Red
        );
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timerActive)
            return;

        var remaining = _timerEnd - _timing.CurTime;

        if (remaining <= TimeSpan.Zero)
        {
            // Timer expired
            _timerActive = false;
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("transmitter-timer-expired"),
                Loc.GetString("transmitter-sender"),
                true,
                colorOverride: Color.Red
            );
            return;
        }

        // 5-minute reminder
        if (!_reminderSent && remaining <= TimeSpan.FromMinutes(5))
        {
            _reminderSent = true;
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("transmitter-reminder-announcement", ("minutes", 5)),
                Loc.GetString("transmitter-sender"),
                true,
                colorOverride: Color.Orange
            );
        }
    }
}
