namespace DenonMarantzAvrNet.Models;

/// <summary>An actual, receiver-reported sound mode entry (OPSMLALL, if supported).</summary>
public sealed class DenonSoundModeOption
{
    public DenonSoundModeOption(string familyCode, string nativeId, bool active, string displayName)
    {
        FamilyCode = familyCode;
        NativeId = nativeId;
        IsActive = active;
        DisplayName = displayName;
    }
    /// <summary>Protocol category MOV, MUS, GAM, PUR.</summary>
    public string FamilyCode { get; }
    public string NativeId { get; }
    public bool IsActive { get; }
    public string DisplayName { get; }
}
