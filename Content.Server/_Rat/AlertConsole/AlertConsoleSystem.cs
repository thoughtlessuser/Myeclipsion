using System;
using System.Collections.Generic;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Rat.AlertConsole;
using Content.Shared.Radio;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Rat.AlertConsole;

public sealed class AlertConsoleSystem : EntitySystem
{
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AlertConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<AlertConsoleComponent, AlertConsoleSaveSettingsMessage>(OnSaveSettings);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var consoleQuery = EntityQueryEnumerator<AlertConsoleComponent, TransformComponent>();
        while (consoleQuery.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!comp.Enabled)
                continue;

            comp.ScanAccumulator += frameTime;
            if (comp.ScanAccumulator < comp.ScanInterval)
                continue;
            comp.ScanAccumulator = 0f;

            if (xform.MapID == Robust.Shared.Map.MapId.Nullspace)
                continue;

            var consolePos = xform.WorldPosition;
            var consoleMap = xform.MapID;
            var now = _timing.CurTime;

            // Purge stale cooldown entries
            var stale = new List<EntityUid>();
            foreach (var (eid, last) in comp.AlertCooldowns)
            {
                if (now - last > TimeSpan.FromSeconds(comp.AlertCooldownSeconds * 3))
                    stale.Add(eid);
            }
            foreach (var s in stale)
                comp.AlertCooldowns.Remove(s);

            // Validate channels once per scan
            var hasFactionChannel = _prototype.TryIndex<RadioChannelPrototype>(comp.FactionChannel, out _);
            var hasCommonChannel = _prototype.TryIndex<RadioChannelPrototype>("Common", out _);

            var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
            while (shuttleQuery.MoveNext(out var shuttleUid, out _, out var shuttleXform))
            {
                if (shuttleXform.MapID != consoleMap)
                    continue;

                // Skip the console's own grid (the station)
                if (xform.GridUid != null && shuttleUid == xform.GridUid)
                    continue;

                var dist = (shuttleXform.WorldPosition - consolePos).Length();
                if (dist > comp.DetectionRadius)
                    continue;

                // Per-shuttle cooldown
                if (comp.AlertCooldowns.TryGetValue(shuttleUid, out var lastAlert) &&
                    now - lastAlert < TimeSpan.FromSeconds(comp.AlertCooldownSeconds))
                    continue;

                comp.AlertCooldowns[shuttleUid] = now;

                var shuttleName = MetaData(shuttleUid).EntityName;
                var distStr = ((int) dist).ToString();

                // Broadcast station/faction alert
                if (hasFactionChannel && !string.IsNullOrWhiteSpace(comp.StationAlertMessage))
                {
                    var msg = comp.StationAlertMessage
                        .Replace("{name}", shuttleName)
                        .Replace("{dist}", distStr);
                    _radio.SendRadioMessage(uid, msg, comp.FactionChannel, uid);
                }

                // Broadcast to Common channel targeting the shuttle
                if (comp.BroadcastToShuttle && hasCommonChannel &&
                    !string.IsNullOrWhiteSpace(comp.ShuttleAlertMessage))
                {
                    var msg = comp.ShuttleAlertMessage
                        .Replace("{name}", shuttleName)
                        .Replace("{dist}", distStr);
                    _radio.SendRadioMessage(uid, msg, "Common", uid);
                }
            }
        }
    }

    private void OnUiOpened(EntityUid uid, AlertConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, comp);
    }

    private void OnSaveSettings(EntityUid uid, AlertConsoleComponent comp, AlertConsoleSaveSettingsMessage args)
    {
        comp.Enabled = args.Enabled;
        comp.DetectionRadius = Math.Clamp(args.DetectionRadius, 10f, 2000f);
        comp.FactionChannel = args.FactionChannel.Trim();
        comp.StationAlertMessage = args.StationAlertMessage;
        comp.BroadcastToShuttle = args.BroadcastToShuttle;
        comp.ShuttleAlertMessage = args.ShuttleAlertMessage;
        comp.AlertCooldownSeconds = Math.Clamp(args.AlertCooldownSeconds, 5f, 3600f);
        Dirty(uid, comp);
        UpdateUiState(uid, comp);
    }

    private void UpdateUiState(EntityUid uid, AlertConsoleComponent comp)
    {
        var state = new AlertConsoleBuiState(
            comp.Enabled,
            comp.DetectionRadius,
            comp.FactionChannel,
            comp.StationAlertMessage,
            comp.BroadcastToShuttle,
            comp.ShuttleAlertMessage,
            comp.AlertCooldownSeconds);
        _uiSystem.SetUiState(uid, AlertConsoleUiKey.Key, state);
    }
}
