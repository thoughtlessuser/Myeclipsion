using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Rat.SignalTransmitter;

[RegisterComponent]
public sealed partial class SignalTransmitterDeviceComponent : Component
{
    /// <summary>
    /// Whether the transmitter has already been activated.
    /// </summary>
    [DataField]
    public bool Activated;

    /// <summary>
    /// Total countdown duration in minutes.
    /// </summary>
    [DataField]
    public int TimerMinutes = 10;

    /// <summary>
    /// Reminder time in minutes remaining.
    /// </summary>
    [DataField]
    public int ReminderMinutes = 5;
}
