namespace DenonMarantzAvrNet.Models;

/// <summary>
/// One persistent speaker-setup calibration level read from a legacy receiver's
/// Speakers/Levels HTML page. Not a Telnet CV? trim or OPTION Channel Level Adjust.
/// Channel names and form fields come directly from the HTML, not a model profile.
/// </summary>
public sealed record DenonLegacySpeakerCalibrationLevel(
    string NativeChannelCode,
    string DisplayName,
    string FormFieldName,
    double Decibels)
{
    /// <summary>A known channel flag, or null when this receiver reports an unknown code.</summary>
    public SpeakerChannel? Channel
    {
        get
        {
            var channel = SpeakerChannelParser.Parse(new[] { NativeChannelCode });
            return channel == SpeakerChannel.None ? null : channel;
        }
    }
}

/// <summary>
/// One persistent distance read from the legacy Speakers/Distances HTML page.
/// Meters is always normalized; OriginalValue/ReceiverUnit preserve the receiver display.
/// </summary>
public sealed record DenonLegacySpeakerCalibrationDistance(
    string NativeChannelCode,
    string DisplayName,
    string FormFieldName,
    double Meters,
    double OriginalValue,
    DenonSpeakerDistanceUnit ReceiverUnit)
{
    public SpeakerChannel? Channel
    {
        get
        {
            var channel = SpeakerChannelParser.Parse(new[] { NativeChannelCode });
            return channel == SpeakerChannel.None ? null : channel;
        }
    }
}

/// <summary>
/// Persistent speaker setup as read by HTTP GET from the legacy receiver web UI.
/// A receiver may expose these pages without having multiple speaker presets.
/// </summary>
public sealed record DenonLegacySpeakerCalibrationSnapshot(
    int HttpPort,
    DenonSpeakerDistanceUnit ReceiverDistanceUnit,
    IReadOnlyList<DenonLegacySpeakerCalibrationLevel> Levels,
    IReadOnlyList<DenonLegacySpeakerCalibrationDistance> Distances);
