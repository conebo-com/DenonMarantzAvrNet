using DenonMarantzAvrNet.Models;

namespace DenonMarantzAvrNet;

/// <summary>
/// Distinguishes a listening-mode family event (SSSMG) from the actual codec
/// (MS). A mode change alone does not prove which family was selected.
/// </summary>
internal static class DenonSoundModeFamilyVerifier
{
    internal static string GetCommand(DenonSoundModeFamily family) => family switch
    {
        DenonSoundModeFamily.Movie => "MSMOVIE",
        DenonSoundModeFamily.Music => "MSMUSIC",
        DenonSoundModeFamily.Game => "MSGAME",
        DenonSoundModeFamily.Direct => "MSDIRECT",
        DenonSoundModeFamily.PureDirect => "MSPURE DIRECT",
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    internal static bool IsMatchingFamilyNotification(DenonSoundModeFamily family, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var expected = family switch
        {
            DenonSoundModeFamily.Movie => "MOV",
            DenonSoundModeFamily.Music => "MUS",
            DenonSoundModeFamily.Game => "GAM",
            DenonSoundModeFamily.Direct or DenonSoundModeFamily.PureDirect => "PUR",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        return string.Equals(line.Trim(), "SSSMG " + expected, StringComparison.OrdinalIgnoreCase);
    }

    internal static DenonSoundModeVerification Evaluate(DenonSoundModeFamily family,
        string previousMode, string currentMode, bool matchingFamilyNotification)
    {
        if (!IsModeStatus(currentMode)) return DenonSoundModeVerification.Unverified;
        if (family == DenonSoundModeFamily.Direct &&
            string.Equals(currentMode.Trim(), "MSDIRECT", StringComparison.OrdinalIgnoreCase))
            return DenonSoundModeVerification.Confirmed;
        if (family == DenonSoundModeFamily.PureDirect &&
            string.Equals(currentMode.Trim(), "MSPURE DIRECT", StringComparison.OrdinalIgnoreCase))
            return DenonSoundModeVerification.Confirmed;
        // PUR is shared between Direct and Pure Direct, so require exact MS
        // confirmation above for both rather than accepting SSSMG PUR alone.
        if ((family is DenonSoundModeFamily.Movie or DenonSoundModeFamily.Music or DenonSoundModeFamily.Game) &&
            matchingFamilyNotification)
            return DenonSoundModeVerification.Confirmed;
        if (!IsModeStatus(previousMode)) return DenonSoundModeVerification.Unverified;
        return DenonSoundModeSelectionResult.IsSameModeStatus(previousMode, currentMode)
            ? DenonSoundModeVerification.Unchanged
            : DenonSoundModeVerification.ModeChangedFamilyUnconfirmed;
    }

    internal static bool IsModeStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) && status!.Trim().Length > 2 &&
        status.Trim().StartsWith("MS", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status.Trim(), "MS?", StringComparison.OrdinalIgnoreCase);
}
