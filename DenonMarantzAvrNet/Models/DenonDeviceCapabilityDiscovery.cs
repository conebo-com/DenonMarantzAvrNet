using System;
using System.Collections.Generic;

namespace DenonMarantzAvrNet.Models;

/// <summary>
/// Receiver-advertised metadata from Deviceinfo.xml. A described option is NOT
/// necessarily configured, active, callable, or verified for writing.
/// No device identifiers (MAC, serial, host) are stored in this model.
/// </summary>
public sealed class DenonDeviceCapabilityDiscovery
{
    public DenonDeviceCapabilityDiscovery(DateTimeOffset capturedUtc, int httpPort,
        IReadOnlyList<DenonAdvertisedCommand> commands,
        IReadOnlyList<DenonAdvertisedChannel> channels,
        IReadOnlyList<DenonAdvertisedSoundMode> soundModes,
        double? maximumVolume, double? volumeStep,
        int? quickSelectCount, int? tunerPresetCount)
    {
        CapturedUtc = capturedUtc;
        HttpPort = httpPort;
        Commands = commands;
        Channels = channels;
        SoundModes = soundModes;
        MaximumVolume = maximumVolume;
        VolumeStep = volumeStep;
        QuickSelectCount = quickSelectCount;
        TunerPresetCount = tunerPresetCount;
    }

    public DateTimeOffset CapturedUtc { get; }
    public int HttpPort { get; }
    /// <summary>Advertised read/write functions, including disabled (0) declarations.</summary>
    public IReadOnlyList<DenonAdvertisedCommand> Commands { get; }
    /// <summary>Possible channel types, NOT the configured or active speaker set.</summary>
    public IReadOnlyList<DenonAdvertisedChannel> Channels { get; }
    /// <summary>Static Main Zone sound mode catalog, NOT current signal-dependent availability.</summary>
    public IReadOnlyList<DenonAdvertisedSoundMode> SoundModes { get; }
    public double? MaximumVolume { get; }
    public double? VolumeStep { get; }
    public int? QuickSelectCount { get; }
    public int? TunerPresetCount { get; }

    public bool IsCommandAdvertised(string name) =>
        System.Linq.Enumerable.Any(Commands, c => c.Advertised &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class DenonAdvertisedCommand
{
    public DenonAdvertisedCommand(string name, string context, bool advertised,
        string? version, string sourceNode)
    {
        Name = name;
        Context = context;
        Advertised = advertised;
        Version = version;
        SourceNode = sourceNode;
    }

    public string Name { get; }
    /// <summary>Relative XML path to avoid ambiguity between zones and functions.</summary>
    public string Context { get; }
    public bool Advertised { get; }
    public string? Version { get; }
    /// <summary>Functions or Commands (both are treated as claims, not verification).</summary>
    public string SourceNode { get; }
}

public sealed class DenonAdvertisedChannel
{
    public DenonAdvertisedChannel(string nativeCode, string? displayName,
        double? minimum, double? maximum, double? defaultValue, double? step)
    {
        NativeCode = nativeCode;
        DisplayName = displayName;
        Minimum = minimum;
        Maximum = maximum;
        DefaultValue = defaultValue;
        Step = step;
    }
    public string NativeCode { get; }
    public string? DisplayName { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public double? DefaultValue { get; }
    public double? Step { get; }
    /// <summary>A heuristic; entries are preserved and not silently classified as speakers.</summary>
    public bool MayBePlaceholder => Minimum.HasValue && Maximum.HasValue &&
        Minimum.Value == Maximum.Value;
}

public sealed class DenonAdvertisedSoundMode
{
    public DenonAdvertisedSoundMode(string group, string name)
    {
        Group = group;
        Name = name;
    }
    public string Group { get; }
    public string Name { get; }
}

/// <summary>Evidence for a read-only command probe. Never implies write support.</summary>
public sealed class DenonReadCommandProbe
{
    public DenonReadCommandProbe(string command, string endpoint, int port,
        string status, IReadOnlyList<string> responseFields, string? note)
    {
        Command = command;
        Endpoint = endpoint;
        HttpPort = port;
        Status = status;
        ResponseFields = responseFields;
        Note = note;
    }
    public string Command { get; }
    public string Endpoint { get; }
    public int HttpPort { get; }
    /// <summary>Received, Empty, Rejected, Error. Received is not a semantic value validation.</summary>
    public string Status { get; }
    /// <summary>Only XML element paths / param names, never user-provided values.</summary>
    public IReadOnlyList<string> ResponseFields { get; }
    public string? Note { get; }
}
