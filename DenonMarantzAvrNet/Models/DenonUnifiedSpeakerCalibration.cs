namespace DenonMarantzAvrNet.Models;

/// <summary>One channel of persistent setup calibration; never the OPTION/CV trim.</summary>
public sealed record DenonCalibrationChannel(
    string NativeChannelCode, string DisplayName,
    double? LevelDecibels, double? DistanceMeters,
    int? LevelIndex = null, int? DistanceIndex = null);

/// <summary>
/// Unified, read-only snapshot of the currently selected speaker setup/preset.
/// Legacy HTML and modern AJAX are independent transports.
/// A missing value remains null; unknown channels are never dropped.
/// </summary>
public sealed record DenonSpeakerCalibrationSnapshot(
    string Source, DateTimeOffset CapturedUtc,
    IReadOnlyList<DenonCalibrationChannel> Channels);

/// <summary>Actual receiver response and status after a family-button command.</summary>
public sealed record DenonSoundModeSelectionResult(
    DenonSoundModeFamily Family, string CommandResponse, string CurrentModeResponse)
{
    /// <summary>A matching MS status was returned; no assumption about which codec is active.</summary>
    public bool HasModeStatus => CurrentModeResponse.StartsWith("MS", StringComparison.OrdinalIgnoreCase);
}
