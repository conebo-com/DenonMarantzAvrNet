namespace DenonMarantzAvrNet;

/// <summary>
/// Describes a logical library operation. Use <see cref="DenonAvrClient.GetSupportedProtocols"/>
/// to determine which network transport implements an operation.
/// </summary>
public enum AvrFeature
{
    MainZonePower,
    MainZoneVolume,
    MainZoneMute,
    MainZoneInput,
    MainZoneStatus,
    Zone2Control,
    Zone3Control,
    LiveEvents,
    ChannelLevelRead,
    ChannelLevelControl,
    SpeakerPresetLevelControl,
    SpeakerDistanceControl,
    AudioInformation,
    ActiveSpeakerStatus,
    SpeakerPresetControl,
    SurroundModeControl,
    DigitalInputModeControl,
    /// <summary>Reads and switches the active speaker preset through the receiver's speaker setup web API.</summary>
    SpeakerPresetSelection,

    // Zone power and input are Telnet-only; Zone2Control/Zone3Control cover volume and mute.
    Zone2Power,
    Zone2Input,
    Zone3Power,
    Zone3Input
}