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
    /// <summary>
    /// Returns true only when a full status after restoration matches the original
    /// MS status; different sound modes (even with the same MS prefix) never match.
    /// </summary>
    public static bool IsSameModeStatus(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            return false;
        var before = expected.Trim();
        var after = actual.Trim();
        return before.Length > 2 && before.StartsWith("MS", StringComparison.OrdinalIgnoreCase) &&
            after.Length > 2 && after.StartsWith("MS", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A matching MS status was returned; no assumption about which codec is active.</summary>
    public bool HasModeStatus => CurrentModeResponse.StartsWith("MS", StringComparison.OrdinalIgnoreCase);
}
