using System;
using System.Collections.Generic;

namespace DenonMarantzAvrNet.Models;

/// <summary>
/// The device-reported slots of the GetActiveSpeaker HTTP status command.
/// These are NOT speaker calibration indices, trims, or distances.
/// A slot can be present even when it is not currently outputting audio.
/// </summary>
public sealed class DenonSpeakerMatrixDiscovery
{
    public DenonSpeakerMatrixDiscovery(DateTimeOffset capturedUtc, int? httpPort,
        string status, IReadOnlyList<DenonSpeakerMatrixSlot> slots)
    {
        CapturedUtc = capturedUtc;
        HttpPort = httpPort;
        Status = status;
        Slots = slots;
    }

    public DateTimeOffset CapturedUtc { get; }
    /// <summary>The AppCommand0300 HTTP port that actually returned the status, if any.</summary>
    public int? HttpPort { get; }
    /// <summary>Received, Empty, Rejected, or NoResponse. NoResponse does not mean unsupported.</summary>
    public string Status { get; }
    /// <summary>Receiver-provided slots, in receiver order; no model index table is applied.</summary>
    public IReadOnlyList<DenonSpeakerMatrixSlot> Slots { get; }
}

public sealed class DenonSpeakerMatrixSlot
{
    public DenonSpeakerMatrixSlot(string nativeId, string? label, string? controlCode)
    {
        NativeId = nativeId;
        Label = label;
        ControlCode = controlCode;
    }

    /// <summary>The receiver's parameter identifier (for example, activespa1).</summary>
    public string NativeId { get; }
    /// <summary>Label received from the AVR, if provided; may be absent or proprietary.</summary>
    public string? Label { get; }
    /// <summary>Receiver's original control attribute; meaning is not universally verified.</summary>
    public string? ControlCode { get; }
}
