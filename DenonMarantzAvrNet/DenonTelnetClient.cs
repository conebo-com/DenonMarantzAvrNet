using DenonMarantzAvrNet.Exceptions;
using DenonMarantzAvrNet.Logger;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Profiles;
using DenonMarantzAvrNet.Protocol;
using System.Net.Sockets;
using System.Text;

namespace DenonMarantzAvrNet;

/// <summary>Provides Denon's TCP/IP control protocol, normally available on port 23.</summary>
public sealed class DenonTelnetClient
{
    private readonly TimeSpan _timeout;

    /// <summary>Creates a Telnet protocol client for a receiver host.</summary>
    public DenonTelnetClient(string host, TimeSpan? requestTimeout = null)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(host, nameof(host));
        Host = host.Trim().Trim('[', ']');
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(3);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }
    }

    /// <summary>Gets the receiver hostname or IP address.</summary>
    public string Host { get; }

    /// <summary>
    /// Sends a command on the receiver's shared Telnet connection and waits for
    /// a matching protocol-family status response. Unrelated status messages
    /// and banners are not returned as acknowledgements.
    /// </summary>
    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);
        var lines = await DenonTelnetSession.ForHost(Host)
            .ExecuteAsync(command, null, _timeout, cancellationToken).ConfigureAwait(false);
        return lines[0];
    }

    private static void ValidateCommand(string command)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(command, nameof(command));
        if (command.Contains('\r') || command.Contains('\n'))
            throw new ArgumentException("Ein Telnet-Befehl darf keinen Zeilenumbruch enthalten.", nameof(command));
    }

    /// <summary>Queries the current Zone 2 state.</summary>
    public Task<string> QueryZone2Async(CancellationToken cancellationToken = default) =>
        SendCommandAsync("Z2?", cancellationToken);

    /// <summary>Queries the current Zone 3 state.</summary>
    public Task<string> QueryZone3Async(CancellationToken cancellationToken = default) =>
        SendCommandAsync("Z3?", cancellationToken);

    /// <summary>Queries the Main Zone power state.</summary>
    public Task<string> QueryPowerAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("PW?", cancellationToken);

    /// <summary>Queries the Main Zone master volume.</summary>
    public Task<string> QueryVolumeAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("MV?", cancellationToken);

    /// <summary>Queries the Main Zone mute state.</summary>
    public Task<string> QueryMuteAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("MU?", cancellationToken);

    /// <summary>Queries the selected Main Zone input.</summary>
    public Task<string> QueryInputAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("SI?", cancellationToken);

    /// <summary>Queries the current surround mode.</summary>
    public Task<string> QuerySurroundModeAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("MS?", cancellationToken);

    /// <summary>Queries the configured digital input decoder mode.</summary>
    public Task<string> QueryDigitalInputModeAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("DC?", cancellationToken);

    /// <summary>Switches the Main Zone on.</summary>
    public Task<string> PowerOnAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("PWON", cancellationToken);

    /// <summary>Switches the Main Zone to standby.</summary>
    public Task<string> PowerOffAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("PWOFF", cancellationToken);

    /// <summary>Raises the Main Zone volume by one receiver step.</summary>
    public Task<string> VolumeUpAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("MVUP", cancellationToken);

    /// <summary>Lowers the Main Zone volume by one receiver step.</summary>
    public Task<string> VolumeDownAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("MVDOWN", cancellationToken);

    /// <summary>Sets Main Zone volume on Denon's absolute 0..98 scale.</summary>
    public Task<string> SetVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendCommandAsync($"MV{ToTelnetVolumeValue(volume)}", cancellationToken);

    /// <summary>Enables or disables Main Zone muting.</summary>
    public Task<string> SetMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SendCommandAsync(muted ? "MUON" : "MUOFF", cancellationToken);

    /// <summary>Selects a Main Zone input using a display or Denon protocol name.</summary>
    public Task<string> SetInputAsync(string input, CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(input, nameof(input));
        if (input.Contains('\r') || input.Contains('\n'))
        {
            throw new ArgumentException("Der Eingangsname darf keinen Zeilenumbruch enthalten.", nameof(input));
        }

        return SendCommandAsync($"SI{DenonInputSource.ToProtocolName(input.Trim())}", cancellationToken);
    }

    /// <summary>Switches Zone 2 on or off.</summary>
    public Task<string> SetZone2PowerAsync(bool on, CancellationToken cancellationToken = default) =>
        SendCommandAsync(on ? "Z2ON" : "Z2OFF", cancellationToken);

    /// <summary>Switches Zone 3 on or off.</summary>
    public Task<string> SetZone3PowerAsync(bool on, CancellationToken cancellationToken = default) =>
        SendCommandAsync(on ? "Z3ON" : "Z3OFF", cancellationToken);

    /// <summary>Raises or lowers Zone 2 volume by one receiver step.</summary>
    public Task<string> ChangeZone2VolumeAsync(bool increase, CancellationToken cancellationToken = default) =>
        SendCommandAsync(increase ? "Z2UP" : "Z2DOWN", cancellationToken);

    /// <summary>Raises or lowers Zone 3 volume by one receiver step.</summary>
    public Task<string> ChangeZone3VolumeAsync(bool increase, CancellationToken cancellationToken = default) =>
        SendCommandAsync(increase ? "Z3UP" : "Z3DOWN", cancellationToken);

    /// <summary>Sets Zone 2 volume on Denon's absolute 0..98 scale.</summary>
    public Task<string> SetZone2VolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendCommandAsync($"Z2{ToTelnetVolumeValue(volume)}", cancellationToken);

    /// <summary>Sets Zone 3 volume on Denon's absolute 0..98 scale.</summary>
    public Task<string> SetZone3VolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SendCommandAsync($"Z3{ToTelnetVolumeValue(volume)}", cancellationToken);

    /// <summary>Enables or disables Zone 2 muting.</summary>
    public Task<string> SetZone2MuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SendCommandAsync(muted ? "Z2MUON" : "Z2MUOFF", cancellationToken);

    /// <summary>Enables or disables Zone 3 muting.</summary>
    public Task<string> SetZone3MuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SendCommandAsync(muted ? "Z3MUON" : "Z3MUOFF", cancellationToken);

    /// <summary>Selects a Zone 2 input using a display or Denon protocol name.</summary>
    public Task<string> SetZone2InputAsync(string input, CancellationToken cancellationToken = default) =>
        SetZoneInputAsync("Z2", input, cancellationToken);

    /// <summary>Selects a Zone 3 input using a display or Denon protocol name.</summary>
    public Task<string> SetZone3InputAsync(string input, CancellationToken cancellationToken = default) =>
        SetZoneInputAsync("Z3", input, cancellationToken);

    /// <summary>Switches the receiver's configured Speaker Preset 1 or 2.</summary>
    public Task<string> SelectSpeakerPresetAsync(int preset, CancellationToken cancellationToken = default)
    {
        if (preset is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                "Der Receiver unterstützt Speaker Preset 1 oder 2.");
        }

        return SendCommandAsync($"SPPR {preset}", cancellationToken);
    }

    /// <summary>
    /// Sets a surround mode, for example <c>Dolby Surround</c>, <c>DTS Neural:X</c>,
    /// <c>Stereo</c>, <c>Multi Ch Stereo</c>, <c>Pure Direct</c> or <c>Auto</c>.
    /// </summary>
    public Task<string> SetSurroundModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(mode, nameof(mode));
        if (mode.Contains('\r') || mode.Contains('\n'))
        {
            throw new ArgumentException("Der Soundmodus darf keinen Zeilenumbruch enthalten.", nameof(mode));
        }

        var protocolMode = mode.Trim().ToUpperInvariant() switch
        {
            "AUTO" => "AUTO",
            "STEREO" => "STEREO",
            "PURE DIRECT" => "PURE DIRECT",
            "DOLBY SURROUND" => "DOLBY SURROUND",
            "DTS NEURAL:X" => "DTS NEURAL:X",
            "MULTI CH STEREO" or "MCH STEREO" => "MCH STEREO",
            _ => mode.Trim()
        };

        return SendCommandAsync($"MS{protocolMode}", cancellationToken);
    }

    /// <summary>
    /// Selects a listening-mode family as on the receiver's MOVIE/MUSIC/GAME/PURE
    /// remote buttons. The receiver decides which mode is applicable to the input.
    /// No assumption is made that the command cycles or that all families are supported.
    /// </summary>
    public Task<string> SelectSoundModeFamilyAsync(
        DenonSoundModeFamily family, CancellationToken cancellationToken = default)
    {
        string command;
        switch (family)
        {
            case DenonSoundModeFamily.Movie: command = "MSMOVIE"; break;
            case DenonSoundModeFamily.Music: command = "MSMUSIC"; break;
            case DenonSoundModeFamily.Game: command = "MSGAME"; break;
            case DenonSoundModeFamily.Direct: command = "MSDIRECT"; break;
            case DenonSoundModeFamily.PureDirect: command = "MSPURE DIRECT"; break;
            default: throw new ArgumentOutOfRangeException(nameof(family));
        }
        return SendCommandAsync(command, cancellationToken);
    }

    /// <summary>
    /// Reads the available grouped listening modes when the receiver supports
    /// the optional OPSMLALL protocol. Unsupported receivers may time out.
    /// </summary>
    public async Task<IReadOnlyList<DenonSoundModeOption>> GetSoundModeOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var lines = await SendCommandUntilAsync("OPSMLALL?", "OPSMLALL END", cancellationToken)
            .ConfigureAwait(false);
        return Discovery.DenonSoundModeListReader.Parse(lines);
    }

    /// <summary>Sets the digital input decoder to <c>Auto</c>, <c>PCM</c> or <c>DTS</c>.</summary>
    public Task<string> SetDigitalInputModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(mode, nameof(mode));

        var protocolMode = mode.Trim().ToUpperInvariant() switch
        {
            "AUTO" => "AUTO",
            "PCM" => "PCM",
            "DTS" => "DTS",
            _ => throw new ArgumentException(
                "Als digitaler Eingangsmodus sind nur Auto, PCM oder DTS zulässig.",
                nameof(mode))
        };

        return SendCommandAsync($"DC{protocolMode}", cancellationToken);
    }

    /// <summary>
    /// Returns every receiver-confirmed line of the read-only CV? query, including
    /// CVEND. Do not equate missing channels with unconfigured speakers: this is
    /// the current channel-level-adjustment list, not Audyssey calibration.
    /// </summary>
    public Task<IReadOnlyList<string>> QueryRawChannelLevelsAsync(
        CancellationToken cancellationToken = default) =>
        SendCommandUntilAsync("CV?", "CVEND", cancellationToken);

    /// <summary>Reads one channel level through its specific Denon CV command.</summary>
    public async Task<DenonSpeakerLevel> GetSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        CancellationToken cancellationToken = default)
    {
        var code = DenonSpeakerLevelChannelConverter.ToProtocolCode(channel);
        var response = await SendCommandAsync($"CV{code}?", cancellationToken).ConfigureAwait(false);
        return ParseSpeakerLevel(response);
    }

    /// <summary>
    /// Reads all channel levels configured on the receiver. The receiver terminates the
    /// multi-line response with <c>CVEND</c>; absent/unconfigured channels are not returned.
    /// </summary>
    public async Task<IReadOnlyList<DenonSpeakerLevel>> GetSpeakerLevelsAsync(
        CancellationToken cancellationToken = default)
    {
        var responses = await SendCommandUntilAsync("CV?", "CVEND", cancellationToken).ConfigureAwait(false);
        return responses
            .Where(response => response.StartsWith("CV", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(response, "CVEND", StringComparison.OrdinalIgnoreCase))
            .Select(ParseSpeakerLevel)
            .ToArray();
    }

    /// <summary>Sets a channel level from -12.0 through +12.0 dB in half-decibel steps.</summary>
    public Task<string> SetSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        double decibels,
        CancellationToken cancellationToken = default)
    {
        var code = DenonSpeakerLevelChannelConverter.ToProtocolCode(channel);
        return SendCommandAsync($"CV{code} {ToChannelLevelValue(decibels)}", cancellationToken);
    }

    /// <summary>Raises or lowers a channel level by one receiver step.</summary>
    public Task<string> ChangeSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        bool increase,
        CancellationToken cancellationToken = default)
    {
        var code = DenonSpeakerLevelChannelConverter.ToProtocolCode(channel);
        return SendCommandAsync($"CV{code} {(increase ? "UP" : "DOWN")}", cancellationToken);
    }

    /// <summary>Switches a subwoofer channel level off. Other channels may be rejected by the receiver.</summary>
    public Task<string> SetSpeakerLevelOffAsync(
        DenonSpeakerLevelChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (channel is not (DenonSpeakerLevelChannel.Subwoofer or
                            DenonSpeakerLevelChannel.Subwoofer2 or
                            DenonSpeakerLevelChannel.Subwoofer3 or
                            DenonSpeakerLevelChannel.Subwoofer4))
        {
            throw new ArgumentException("Nur Subwoofer-Kanäle können auf OFF gesetzt werden.", nameof(channel));
        }

        var code = DenonSpeakerLevelChannelConverter.ToProtocolCode(channel);
        return SendCommandAsync($"CV{code} 00", cancellationToken);
    }

    /// <summary>Resets all channel levels to Denon's receiver factory defaults.</summary>
    public Task<string> ResetSpeakerLevelsToFactoryDefaultsAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync("CVZRL", cancellationToken);

    private Task<string> SetZoneInputAsync(
        string zonePrefix,
        string input,
        CancellationToken cancellationToken)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(input, nameof(input));
        if (input.Contains('\r') || input.Contains('\n'))
        {
            throw new ArgumentException("Der Eingangsname darf keinen Zeilenumbruch enthalten.", nameof(input));
        }

        var protocolName = DenonInputSource.ToProtocolName(input.Trim());
        return SendCommandAsync($"{zonePrefix}{protocolName}", cancellationToken);
    }

    private static string ToTelnetVolumeValue(double volume) =>
        DenonVolumeCodec.ToTelnetValue(volume);

    private Task<IReadOnlyList<string>> SendCommandUntilAsync(
        string command, string terminatingResponse, CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        return DenonTelnetSession.ForHost(Host)
            .ExecuteAsync(command, terminatingResponse, _timeout, cancellationToken);
    }

    private static DenonSpeakerLevel ParseSpeakerLevel(string response)
    {
        if (!response.StartsWith("CV", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"Keine CV-Antwort: '{response}'.");
        }

        var parts = response.Substring(2).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !DenonSpeakerLevelChannelConverter.TryFromProtocolCode(parts[0], out var channel))
        {
            throw new FormatException($"Unbekannte CV-Antwort: '{response}'.");
            // <-----------
        }

        if (parts[1] == "00")
        {
            return new DenonSpeakerLevel(channel, null, true, response);
        }

        if (!int.TryParse(parts[1], out var encodedLevel))
        {
            throw new FormatException($"Ungültiger Kanalpegel in '{response}'.");
        }

        var decibels = parts[1].Length == 3 ? encodedLevel / 10.0 - 50.0 : encodedLevel - 50.0;
        return new DenonSpeakerLevel(channel, decibels, false, response);
    }

    private static string ToChannelLevelValue(double decibels)
    {
        if (decibels is < -12.0 or > 12.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decibels), decibels, "Der Kanalpegel muss zwischen -12,0 und +12,0 dB liegen.");
        }

        var roundedLevel = Math.Round(decibels * 2, MidpointRounding.ToEven) / 2.0;
        var encodedLevel = roundedLevel + 50.0;
        var wholeValue = (int)Math.Floor(encodedLevel);
        return encodedLevel - wholeValue >= 0.5 ? $"{wholeValue:00}5" : $"{wholeValue:00}";
    }
}
