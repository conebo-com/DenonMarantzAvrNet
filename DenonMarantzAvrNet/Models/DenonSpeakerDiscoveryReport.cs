namespace DenonMarantzAvrNet.Models;

/// <summary>
/// One native entry observed in a specific receiver AJAX response. Indices from different
/// endpoints are independent; an index is not a globally stable speaker identifier.
/// </summary>
public sealed record DenonSpeakerDiscoveryEntry(
    int NativeIndex,
    string RawValue,
    int? DisplayCode,
    string? ReceiverLabel,
    SpeakerChannel? ProfileChannelHint);

/// <summary>One speaker setup menu item reported by the receiver itself.</summary>
public sealed record DenonSpeakerDiscoveryMenuItem(string Name, string RawStatus);

/// <summary>
/// Read-only snapshot of native speaker indices and available setup metadata.
/// Neither configured nor active playback channels are inferred from an index alone.
/// </summary>
public sealed record DenonSpeakerDiscoveryReport(
    DateTimeOffset CapturedUtc,
    string ReceiverProfileId,
    IReadOnlyList<DenonSpeakerDiscoveryEntry> LevelEntries,
    IReadOnlyList<DenonSpeakerDiscoveryEntry> DistanceEntries,
    IReadOnlyList<DenonSpeakerDiscoveryEntry> ConfigurationEntries,
    IReadOnlyList<DenonSpeakerDiscoveryEntry> LayoutGroupEntries,
    IReadOnlyList<DenonSpeakerDiscoveryMenuItem> MenuItems,
    string? DistanceUnitRaw,
    string? DistanceStepRaw,
    IReadOnlyList<string> Warnings);
