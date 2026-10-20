namespace DenonMarantzAvrNet.Models;

/// <summary>
/// Per-input Channel Level Adjust (OPTION menu), NOT the persistent Audyssey/Manual Setup speaker levels.
/// Raw values are preserved because different receiver generations can use different encodings.
/// </summary>
public sealed class DenonChannelLevelAdjustment
{
    public DenonChannelLevelAdjustment(string channel, string? status, string? speakerType,
        string? levelRaw, string? valueRaw)
    {
        Channel = channel;
        Status = status;
        SpeakerType = speakerType;
        LevelRaw = levelRaw;
        ValueRaw = valueRaw;
    }

    public string Channel { get; }
    public string? Status { get; }
    public string? SpeakerType { get; }
    public string? LevelRaw { get; }
    public string? ValueRaw { get; }
}
