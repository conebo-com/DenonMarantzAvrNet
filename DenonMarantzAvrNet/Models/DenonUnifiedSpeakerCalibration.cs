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

/// <summary>Result of checking a sound-mode family command independently of its SET echo.</summary>
public enum DenonSoundModeVerification
{
    /// <summary>The AVR reported the requested family or the exact Direct/Pure Direct mode.</summary>
    Confirmed,
    /// <summary>MS? changed, but no family-specific acknowledgement was observed.</summary>
    ModeChangedFamilyUnconfirmed,
    /// <summary>MS? was unchanged and no family acknowledgement was observed.</summary>
    Unchanged,
    /// <summary>A fresh MS? status could not be obtained.</summary>
    Unverified
}

/// <summary>Actual receiver response and status after a family-button command.</summary>
public sealed record DenonSoundModeSelectionResult(
    DenonSoundModeFamily Family, string CommandResponse, string CurrentModeResponse)
{
    /// <summary>How strongly the AVR response supports the requested family switch.</summary>
    public DenonSoundModeVerification Verification { get; init; } = DenonSoundModeVerification.Unverified;

    /// <summary>MS? status captured before sending the command, if available.</summary>
    public string PreviousModeResponse { get; init; } = string.Empty;

    /// <summary>Matching SSSMG family event, if received; not a specific codec.</summary>
    public string? FamilyNotification { get; init; }

    /// <summary>True when the receiver explicitly confirmed the family/mode.</summary>
    public bool IsConfirmed => Verification == DenonSoundModeVerification.Confirmed;

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
