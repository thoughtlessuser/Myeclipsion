using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Rat.AlertConsole;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
public sealed partial class AlertConsoleComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public float DetectionRadius = 200f;

    [DataField, AutoNetworkedField]
    public string FactionChannel = "Common";

    [DataField, AutoNetworkedField]
    public string StationAlertMessage = "{name} приближается к станции на {dist} метров!";

    [DataField, AutoNetworkedField]
    public bool BroadcastToShuttle = true;

    [DataField, AutoNetworkedField]
    public string ShuttleAlertMessage = "{name}, вы вошли в охраняемую зону. Назовите принадлежность к фракции и цель визита.";

    [DataField, AutoNetworkedField]
    public float AlertCooldownSeconds = 60f;

    [DataField]
    public float ScanInterval = 5f;

    [ViewVariables]
    public float ScanAccumulator = 0f;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> AlertCooldowns = new();
}

[Serializable, NetSerializable]
public enum AlertConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class AlertConsoleBuiState : BoundUserInterfaceState
{
    public readonly bool Enabled;
    public readonly float DetectionRadius;
    public readonly string FactionChannel;
    public readonly string StationAlertMessage;
    public readonly bool BroadcastToShuttle;
    public readonly string ShuttleAlertMessage;
    public readonly float AlertCooldownSeconds;

    public AlertConsoleBuiState(
        bool enabled,
        float detectionRadius,
        string factionChannel,
        string stationAlertMessage,
        bool broadcastToShuttle,
        string shuttleAlertMessage,
        float alertCooldownSeconds)
    {
        Enabled = enabled;
        DetectionRadius = detectionRadius;
        FactionChannel = factionChannel;
        StationAlertMessage = stationAlertMessage;
        BroadcastToShuttle = broadcastToShuttle;
        ShuttleAlertMessage = shuttleAlertMessage;
        AlertCooldownSeconds = alertCooldownSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class AlertConsoleSaveSettingsMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;
    public readonly float DetectionRadius;
    public readonly string FactionChannel;
    public readonly string StationAlertMessage;
    public readonly bool BroadcastToShuttle;
    public readonly string ShuttleAlertMessage;
    public readonly float AlertCooldownSeconds;

    public AlertConsoleSaveSettingsMessage(
        bool enabled,
        float detectionRadius,
        string factionChannel,
        string stationAlertMessage,
        bool broadcastToShuttle,
        string shuttleAlertMessage,
        float alertCooldownSeconds)
    {
        Enabled = enabled;
        DetectionRadius = detectionRadius;
        FactionChannel = factionChannel;
        StationAlertMessage = stationAlertMessage;
        BroadcastToShuttle = broadcastToShuttle;
        ShuttleAlertMessage = shuttleAlertMessage;
        AlertCooldownSeconds = alertCooldownSeconds;
    }
}
