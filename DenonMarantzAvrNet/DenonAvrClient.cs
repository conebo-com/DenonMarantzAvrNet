using System.Globalization;
using System.Net.Sockets;
using System.Net.Http;
using DenonMarantzAvrNet.Protocol;
using DenonMarantzAvrNet.Transport;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Profiles;
using DenonMarantzAvrNet.Exceptions;
using DenonMarantzAvrNet.Discovery;

namespace DenonMarantzAvrNet;

/// <summary>Controls a Denon or compatible Marantz receiver through its HTTP/XML API.</summary>
public sealed class DenonAvrClient : IDisposable
{
    /// <summary>Highest absolute speaker-preset level in dB. Accepted range is +/- this value.</summary>
    public const double SpeakerLevelLimitDecibels = 12.0;

    /// <summary>Lowest value on Denon's absolute 0..98 volume scale.</summary>
    public const double MinVolume = DenonVolumeCodec.MinVolume;

    /// <summary>Highest value on Denon's absolute 0..98 volume scale.</summary>
    public const double MaxVolume = DenonVolumeCodec.MaxVolume;

    /// <summary>Lowest supported relative volume in dB.</summary>
    public const double MinVolumeDecibels = DenonVolumeCodec.MinDecibels;

    /// <summary>Highest supported relative volume in dB.</summary>
    public const double MaxVolumeDecibels = DenonVolumeCodec.MaxDecibels;
    private readonly DenonHttpTransport _httpTransport;
    private readonly DenonTelnetClient _telnetClient;
    private readonly bool _ownsTransport;
    private static readonly ISet<DenonControlProtocol> HttpAndTelnet =
        new HashSet<DenonControlProtocol>
        {
            DenonControlProtocol.Http,
            DenonControlProtocol.Telnet
        };
    private static readonly ISet<DenonControlProtocol> HttpOnly =
        new HashSet<DenonControlProtocol> { DenonControlProtocol.Http };
    private static readonly ISet<DenonControlProtocol> TelnetOnly =
        new HashSet<DenonControlProtocol> { DenonControlProtocol.Telnet };
    private static readonly TimeSpan DefaultSpeakerPresetConfirmationTimeout = TimeSpan.FromSeconds(30);
    private IReadOnlyList<string>? _availableInputs;
    private IDenonReceiverProfile? _receiverProfile;
    private IDenonReceiverProfile? _verifiedSpeakerProfile;
    private bool _modernCalibrationProbeFailed;
    private DenonSpeakerLevelSnapshot? _speakerLevelSnapshot;
    private bool _requiresSequentialAppCommandRequests;
    private bool _disposed;

    /// <summary>Creates a client for a receiver host or IP address.</summary>
    /// <param name="host">Receiver hostname, IP address or HTTP(S) base address.</param>
    /// <param name="requestTimeout">Optional timeout; defaults to thirty seconds.</param>
    public DenonAvrClient(string host, TimeSpan? requestTimeout = null)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(host, nameof(host));

        Host = NormalizeHost(host);
        _httpTransport = new DenonHttpTransport(requestTimeout ?? TimeSpan.FromSeconds(30));
        _telnetClient = new DenonTelnetClient(Host, requestTimeout);
        _ownsTransport = true;
    }

    internal DenonAvrClient(string host, DenonHttpTransport httpTransport)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(host, nameof(host));

        Host = NormalizeHost(host);
        _httpTransport = httpTransport ?? throw new ArgumentNullException(nameof(httpTransport));
        _telnetClient = new DenonTelnetClient(Host);
    }

    /// <summary>Gets the normalized receiver hostname or IP address.</summary>
    public string Host { get; }

    /// <summary>
    /// Gets or sets the default transport for control methods. The default <see cref="DenonControlProtocol.Auto"/>
    /// retains HTTP control after initialization and makes Telnet-only receivers usable without HTTP initialization.
    /// </summary>
    public DenonControlProtocol PreferredControlProtocol { get; set; } = DenonControlProtocol.Auto;

    /// <summary>Gets the HTTP port detected by <see cref="InitializeAsync"/>.</summary>
    public int? HttpPort { get; private set; }

    /// <summary>Port which actually answered AppCommand.xml, independently discovered.</summary>
    public int? AppCommandPort { get; private set; }

    /// <summary>Port which actually answered AppCommand0300.xml with useful data.</summary>
    public int? AppCommand0300Port { get; private set; }

    /// <summary>Last receiver-advertised capability catalog; no model profile is applied.</summary>
    public DenonDeviceCapabilityDiscovery? DeviceCapabilities { get; private set; }

    /// <summary>Gets the device information returned by the last initialization.</summary>
    public DenonDeviceInfo? DeviceInfo { get; private set; }

    /// <summary>
    /// Gets the detected capabilities of this concrete receiver, or <see langword="null"/>
    /// until <see cref="InitializeAsync"/> has succeeded.
    /// </summary>
    public DenonReceiverCapabilities? ReceiverCapabilities { get; private set; }

    /// <summary>
    /// Gets the HTTP/API profile selected for the connected receiver, or <see langword="null"/>
    /// until <see cref="InitializeAsync"/> has succeeded.
    /// </summary>
    public string? ReceiverProfileId => _receiverProfile?.Id;

    /// <summary>
    /// Gets the number of speaker presets known for the selected receiver profile.
    /// A value of zero means that the profile does not expose speaker presets.
    /// </summary>
    public int SpeakerPresetCount =>
        GetSpeakerTransportProfile().SpeakerPresetCount;

    /// <summary>Gets the most recently confirmed receiver state.</summary>
    public DenonReceiverState? State { get; private set; }

    /// <summary>Detects the receiver API and reads its device information.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The detected device information.</returns>
    /// <exception cref="DenonConnectionException">No supported API endpoint responded.</exception>
    public async Task<DenonDeviceInfo> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        List<Exception> failures = [];

        foreach (var port in new[] { 80, 8080 })
        {
            try
            {
                var xml = await _httpTransport.GetStringAsync(
                    Host,
                    port,
                    DenonEndpoints.DeviceInfo,
                    cancellationToken).ConfigureAwait(false);

                var deviceInfo = DenonXmlParser.ParseDeviceInfo(xml);
                HttpPort = port;
                DeviceInfo = deviceInfo;
                // Parsing metadata is optional and must never stop basic receiver access.
                try
                {
                    DeviceCapabilities = DenonDeviceCapabilityReader.Parse(xml, port);
                }
                catch (Exception exception) when (exception is System.Xml.XmlException ||
                    exception is InvalidDataException)
                {
                    DeviceCapabilities = null;
                }
                _receiverProfile = DenonReceiverProfileRegistry.Select(deviceInfo, port);
                AppCommandPort = null;
                AppCommandPort = await DiscoverAppCommandPortAsync(port, cancellationToken)
                    .ConfigureAwait(false);
                AppCommand0300Port = null;
                ReceiverCapabilities = CreateReceiverCapabilities(
                    deviceInfo, AppCommandPort is not null);
                State = null;
                _availableInputs = null;
                _speakerLevelSnapshot = null;
                _verifiedSpeakerProfile = null;
                _modernCalibrationProbeFailed = false;
                _requiresSequentialAppCommandRequests = false;
                return deviceInfo;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                              exception is HttpRequestException
                                                  or TaskCanceledException
                                                  or DenonProtocolException)
            {
                failures.Add(exception);
            }
        }

        throw new DenonConnectionException(
            $"Der Receiver '{Host}' antwortet weder auf Port 80 noch auf Port 8080 mit gültigen Geräteinformationen.",
            new AggregateException(failures));
    }

    /// <summary>
    /// Replaces the receiver profile selected during <see cref="InitializeAsync"/> with a supported profile,
    /// for example to test a receiver with the profile of a similar model. The override stays active until
    /// the next successful <see cref="InitializeAsync"/> call.
    /// </summary>
    /// <param name="profileId">Profile ID, for example <c>avc-x6800h</c> or <c>avc-x6700h</c>.</param>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    /// <exception cref="ArgumentException">The profile ID is not supported.</exception>
    public void SelectReceiverProfile(string profileId)
    {
        // Throws if disposed or not initialized.
        _ = GetReceiverProfile();

        var profile = DenonReceiverProfileRegistry.FindById(profileId);

        if (profile is null)
        {
            throw new ArgumentException($"Unbekanntes Receiver-Profil '{profileId}'.", nameof(profileId));
            // <-----------
        }

        _receiverProfile = profile;
    }

    /// <summary>Reads and stores a confirmed Main Zone status snapshot.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The newly read receiver state.</returns>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    /// <exception cref="DenonProtocolException">The receiver returned no usable basic state.</exception>
    public async Task<DenonReceiverState> UpdateAsync(CancellationToken cancellationToken = default)
    {
        var port = GetInitializedPort();
        DenonReceiverState mainZone;

        if (ReceiverCapabilities?.SupportsAppCommand == true)
        {
            try
            {
                var availableInputs = _availableInputs ??
                    await RefreshInputsAsync(cancellationToken).ConfigureAwait(false);
                mainZone = await QueryMainZoneStatusAsync(
                    port, availableInputs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                (exception is DenonProtocolException || exception is HttpRequestException))
            {
                // A partially compatible AppCommand endpoint must not break the
                // legacy MainZone status reader. Keep its support flag, because
                // individual commands may still work on this receiver.
                mainZone = await QueryLegacyMainZoneAsync(port, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            mainZone = await QueryLegacyMainZoneAsync(port, cancellationToken).ConfigureAwait(false);
        }

        // Probe 0300 independently of port AND of AppCommand.xml. A port 80
        // receiver may expose detailed status even when the basic API differs.
        var audioInfoXml = (ReceiverCapabilities?.SupportsAppCommand0300 != false)
            ? await TryQueryAppCommand0300Async(
                port,
                DenonAppCommand.CreateDetailedRequest(
                    DenonAppCommand.GetAudioInfo,
                    "inputmode", "output", "signal", "sound", "fs"),
                cancellationToken).ConfigureAwait(false)
            : null;
        var activeSpeakersXml = (ReceiverCapabilities?.SupportsAppCommand0300 != false)
            ? await TryQueryAppCommand0300Async(
                port,
                DenonAppCommand.CreateDetailedRequest(
                    DenonAppCommand.GetActiveSpeaker, "activespall"),
                cancellationToken).ConfigureAwait(false)
            : null;

        var audio = DenonXmlParser.ParseAppCommandAudioInfo(audioInfoXml, activeSpeakersXml);
        State = mainZone with { Audio = audio };
        if (ReceiverCapabilities is { } capabilities)
            ReceiverCapabilities = capabilities with
            {
                SupportsAppCommand0300 = audioInfoXml is not null || activeSpeakersXml is not null
            };
        return State;
    }

    private async Task<DenonReceiverState> QueryLegacyMainZoneAsync(
        int port, CancellationToken cancellationToken)
    {
        var xml = await _httpTransport.GetStringAsync(
            Host, port, DenonEndpoints.MainZoneStatus, cancellationToken).ConfigureAwait(false);
        var state = DenonXmlParser.ParseMainZoneStatus(xml);
        _availableInputs = state.AvailableInputs;
        return state;
    }


    /// <summary>
    /// Returns the transports implemented by this library for the specified logical operation.
    /// This describes the library/protocol layer only; use <see cref="ReceiverCapabilities"/>
    /// or <see cref="IsFeatureAvailable"/> to inspect the connected hardware as well.
    /// </summary>
    public ISet<DenonControlProtocol> GetSupportedProtocols(AvrFeature feature) => feature switch
    {
        AvrFeature.MainZonePower or
        AvrFeature.MainZoneVolume or
        AvrFeature.MainZoneMute or
        AvrFeature.MainZoneInput => HttpAndTelnet,

        AvrFeature.MainZoneStatus or
        AvrFeature.AudioInformation or
        AvrFeature.ActiveSpeakerStatus or
        AvrFeature.SpeakerPresetLevelControl or
        AvrFeature.SpeakerDistanceControl or
        AvrFeature.SpeakerPresetSelection => HttpOnly,

        AvrFeature.Zone2Control or
        AvrFeature.Zone3Control => HttpAndTelnet,

        AvrFeature.LiveEvents or
        AvrFeature.ChannelLevelRead or
        AvrFeature.ChannelLevelControl or
        AvrFeature.SpeakerPresetControl or
        AvrFeature.SurroundModeControl or
        AvrFeature.DigitalInputModeControl or
        AvrFeature.Zone2Power or
        AvrFeature.Zone2Input or
        AvrFeature.Zone3Power or
        AvrFeature.Zone3Input => TelnetOnly,

        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null) // <-----------
    };

    /// <summary>
    /// Returns whether the library can read and/or change a logical feature. This describes the
    /// library only; use <see cref="GetFeatureSupport"/> for the connected hardware.
    /// </summary>
    public AvrFeatureAccess GetFeatureAccess(AvrFeature feature) => feature switch
    {
        AvrFeature.MainZoneStatus or
        AvrFeature.LiveEvents or
        AvrFeature.ChannelLevelRead or
        AvrFeature.AudioInformation or
        AvrFeature.ActiveSpeakerStatus => AvrFeatureAccess.Read,

        AvrFeature.ChannelLevelControl or
        AvrFeature.SpeakerPresetControl or
        AvrFeature.Zone2Power or
        AvrFeature.Zone2Input or
        AvrFeature.Zone3Power or
        AvrFeature.Zone3Input => AvrFeatureAccess.Write,

        AvrFeature.MainZonePower or
        AvrFeature.MainZoneVolume or
        AvrFeature.MainZoneMute or
        AvrFeature.MainZoneInput or
        AvrFeature.Zone2Control or
        AvrFeature.Zone3Control or
        AvrFeature.SpeakerPresetLevelControl or
        AvrFeature.SpeakerDistanceControl or
        AvrFeature.SpeakerPresetSelection or
        AvrFeature.SurroundModeControl or
        AvrFeature.DigitalInputModeControl => AvrFeatureAccess.Read | AvrFeatureAccess.Write,

        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null) // <-----------
    };

    /// <summary>
    /// Determines whether the detected receiver supports a logical feature. Returns
    /// <see langword="false"/> before initialization because the hardware is then unknown.
    /// </summary>
    public bool IsFeatureAvailable(AvrFeature feature)
    {
        var capabilities = ReceiverCapabilities;
        if (capabilities is null)
        {
            return false;
            // <-----------
        }

        return feature switch
        {
            AvrFeature.Zone2Control => capabilities.SupportsZone2 &&
                (capabilities.SupportsHttp || capabilities.SupportsTelnet == true),
            AvrFeature.Zone3Control => capabilities.SupportsZone3 &&
                (capabilities.SupportsHttp || capabilities.SupportsTelnet == true),
            AvrFeature.Zone2Power or AvrFeature.Zone2Input =>
                capabilities.SupportsZone2 && capabilities.SupportsTelnet == true,
            AvrFeature.Zone3Power or AvrFeature.Zone3Input =>
                capabilities.SupportsZone3 && capabilities.SupportsTelnet == true,
            AvrFeature.AudioInformation or AvrFeature.ActiveSpeakerStatus =>
                capabilities.SupportsAppCommand0300 == true,
            AvrFeature.SpeakerPresetLevelControl =>
                GetSpeakerTransportProfile().SupportsSpeakerPresetLevels,
            AvrFeature.SpeakerDistanceControl =>
                GetSpeakerTransportProfile().SupportsSpeakerDistances,
            AvrFeature.SpeakerPresetSelection =>
                GetSpeakerTransportProfile().SupportsSpeakerPresetSelection,
            AvrFeature.MainZonePower or
            AvrFeature.MainZoneVolume or
            AvrFeature.MainZoneMute or
            AvrFeature.MainZoneInput or
            AvrFeature.MainZoneStatus => capabilities.SupportsHttp || capabilities.SupportsTelnet == true,
            AvrFeature.LiveEvents or
            AvrFeature.ChannelLevelRead or
            AvrFeature.ChannelLevelControl or
            AvrFeature.SpeakerPresetControl or
            AvrFeature.SurroundModeControl or
            AvrFeature.DigitalInputModeControl => capabilities.SupportsTelnet == true,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null) // <-----------
        };
        // <-----------
    }

    /// <summary>
    /// Three-state variant of <see cref="IsFeatureAvailable"/>. Returns
    /// <see cref="AvrFeatureSupport.Unknown"/> while support has not been determined yet.
    /// </summary>
    public AvrFeatureSupport GetFeatureSupport(AvrFeature feature)
    {
        ThrowIfDisposed();

        if (!Enum.IsDefined(typeof(AvrFeature), feature))
        {
            throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
            // <-----------
        }

        var capabilities = ReceiverCapabilities;
        if (capabilities is null)
        {
            return AvrFeatureSupport.Unknown;
            // <-----------
        }

        return feature switch
        {
            AvrFeature.MainZonePower or
            AvrFeature.MainZoneVolume or
            AvrFeature.MainZoneMute or
            AvrFeature.MainZoneInput or
            AvrFeature.MainZoneStatus => GetTransportSupport(capabilities),

            AvrFeature.Zone2Control => GetZoneSupport(capabilities, capabilities.SupportsZone2),
            AvrFeature.Zone3Control => GetZoneSupport(capabilities, capabilities.SupportsZone3),

            AvrFeature.Zone2Power or
            AvrFeature.Zone2Input => GetZoneTelnetSupport(capabilities, capabilities.SupportsZone2),
            AvrFeature.Zone3Power or
            AvrFeature.Zone3Input => GetZoneTelnetSupport(capabilities, capabilities.SupportsZone3),

            AvrFeature.AudioInformation or
            AvrFeature.ActiveSpeakerStatus => ToSupport(capabilities.SupportsAppCommand0300),

            AvrFeature.SpeakerPresetLevelControl =>
                GetSpeakerSupport(profile => profile.SupportsSpeakerPresetLevels),
            AvrFeature.SpeakerDistanceControl =>
                GetSpeakerSupport(profile => profile.SupportsSpeakerDistances),
            AvrFeature.SpeakerPresetSelection =>
                GetSpeakerSupport(profile => profile.SupportsSpeakerPresetSelection),

            AvrFeature.LiveEvents or
            AvrFeature.ChannelLevelRead or
            AvrFeature.ChannelLevelControl or
            AvrFeature.SpeakerPresetControl or
            AvrFeature.SurroundModeControl or
            AvrFeature.DigitalInputModeControl => ToSupport(capabilities.SupportsTelnet),

            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null) // <-----------
        };
        // <-----------
    }

    private static AvrFeatureSupport GetZoneTelnetSupport(DenonReceiverCapabilities capabilities, bool zoneSupported)
    {
        if (!zoneSupported)
        {
            return capabilities.ZoneCount is null
                ? AvrFeatureSupport.Unknown
                : AvrFeatureSupport.NotSupported;
            // <-----------
        }

        return ToSupport(capabilities.SupportsTelnet);
        // <-----------
    }

    private static AvrFeatureSupport ToSupport(bool? value) => value switch
    {
        true => AvrFeatureSupport.Supported,
        false => AvrFeatureSupport.NotSupported,
        null => AvrFeatureSupport.Unknown
    };

    private static AvrFeatureSupport GetTransportSupport(DenonReceiverCapabilities capabilities) =>
        capabilities.SupportsHttp
            ? AvrFeatureSupport.Supported
            : ToSupport(capabilities.SupportsTelnet);

    private static AvrFeatureSupport GetZoneSupport(DenonReceiverCapabilities capabilities, bool zoneSupported)
    {
        if (!zoneSupported)
        {
            return capabilities.ZoneCount is null
                ? AvrFeatureSupport.Unknown
                : AvrFeatureSupport.NotSupported;
            // <-----------
        }

        return GetTransportSupport(capabilities);
        // <-----------
    }

    private AvrFeatureSupport GetSpeakerSupport(Func<IDenonReceiverProfile, bool> supportSelector)
    {
        var profile = GetSpeakerTransportProfile();

        if (profile.Id == "unknown" &&
            !_modernCalibrationProbeFailed &&
            CanProbeModernCalibration())
        {
            return AvrFeatureSupport.Unknown;
            // <-----------
        }

        return ToSupport(supportSelector(profile));
        // <-----------
    }

    private bool CanProbeModernCalibration() =>
        HttpPort == 8080 &&
        DeviceInfo is not null &&
        string.Equals(DeviceInfo.CommunicationApiVersion, "0301", StringComparison.Ordinal);

    /// <summary>
    /// Safely probes the Telnet control port and updates <see cref="ReceiverCapabilities"/>.
    /// <see cref="InitializeAsync"/> must have succeeded first so HTTP device information is retained.
    /// </summary>
    public async Task<DenonReceiverCapabilities> ProbeReceiverCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var capabilities = ReceiverCapabilities ?? throw new InvalidOperationException(
            "InitializeAsync muss vor dem Prüfen der Receiver-Fähigkeiten aufgerufen werden.");

        try
        {
            _ = await _telnetClient.SendCommandAsync("PW?", cancellationToken).ConfigureAwait(false);
            ReceiverCapabilities = capabilities with { SupportsTelnet = true };
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is IOException or SocketException or OperationCanceledException)
        {
            ReceiverCapabilities = capabilities with { SupportsTelnet = false };
        }

        return ReceiverCapabilities;
    }

    /// <summary>
    /// Marks AppCommand0300 support as undetermined so the next <see cref="UpdateAsync"/> probes it again,
    /// for example after the receiver left standby. No request is sent by this method.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    public void ResetAppCommand0300Support()
    {
        ThrowIfDisposed();

        var capabilities = ReceiverCapabilities ?? throw new InvalidOperationException(
            "InitializeAsync muss vor dem Zurücksetzen der Receiver-Fähigkeiten aufgerufen werden.");
        // <-----------

        ReceiverCapabilities = capabilities with { SupportsAppCommand0300 = null };
    }

    /// <summary>
    /// Closes the shared Telnet connection to this host. Returns <see langword="false"/> without closing
    /// while a command is running or an event listener is attached. A later command reconnects automatically.
    /// </summary>
    public bool TryCloseTelnetSession()
    {
        ThrowIfDisposed();

        // The Telnet client's host is the session key; it can differ from Host for bracketed IPv6 addresses.
        return DenonTelnetSession.ForHost(_telnetClient.Host).TryClose();
        // <-----------
    }

    /// <summary>Refreshes and returns the receiver's currently enabled input list.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The enabled input display names reported by the receiver.</returns>
    public async Task<IReadOnlyList<string>> RefreshInputsAsync(
        CancellationToken cancellationToken = default)
    {
        var port = GetInitializedPort();
        IReadOnlyList<string>? inputs = null;

        if (ReceiverCapabilities?.SupportsAppCommand == true)
        {
            try
            {
                var xml = await QueryAppCommandAsync(
                    port, DenonAppCommand.GetDeletedSource, cancellationToken).ConfigureAwait(false);
                if (DenonXmlParser.HasAppCommandData(xml))
                {
                    inputs = DenonXmlParser.ParseAppCommandAvailableInputs(xml);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                (exception is HttpRequestException || exception is DenonProtocolException))
            {
                // Use older MainZone input list if this individual command is unsupported.
            }
        }

        if (inputs is null)
        {
            var xml = await _httpTransport.GetStringAsync(
                Host, port, DenonEndpoints.MainZoneStatus, cancellationToken).ConfigureAwait(false);
            inputs = DenonXmlParser.ParseMainZoneStatus(xml).AvailableInputs;
        }

        _availableInputs = inputs;
        if (State is not null)
            State = State with { AvailableInputs = inputs };
        return inputs;
    }

    private async Task<DenonReceiverState> QueryMainZoneStatusAsync(
        int port,
        IReadOnlyList<string> availableInputs,
        CancellationToken cancellationToken)
    {
        if (!_requiresSequentialAppCommandRequests)
        {
            var bundledXml = await _httpTransport.PostXmlAsync(
                Host,
                AppCommandPort ?? port,
                DenonEndpoints.AppCommand,
                DenonAppCommand.CreateRequest(DenonAppCommand.MainZoneStatusCommands),
                cancellationToken).ConfigureAwait(false);

            try
            {
                return DenonXmlParser.ParseBundledAppCommandMainZoneStatus(
                    bundledXml,
                    availableInputs);
            }
            catch (DenonProtocolException)
            {
                // Remember the receiver's behavior so later updates can skip
                // the known-incompatible bundled request.
                _requiresSequentialAppCommandRequests = true;
            }
        }

        return await QuerySequentialMainZoneStatusAsync(
            port,
            availableInputs,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<DenonReceiverState> QuerySequentialMainZoneStatusAsync(
        int port,
        IReadOnlyList<string> availableInputs,
        CancellationToken cancellationToken)
    {
        var powerXml = await QueryAppCommandAsync(
            port,
            DenonAppCommand.GetAllZonePowerStatus,
            cancellationToken).ConfigureAwait(false);
        var volumeXml = await QueryAppCommandAsync(
            port,
            DenonAppCommand.GetAllZoneVolume,
            cancellationToken).ConfigureAwait(false);
        var muteXml = await QueryAppCommandAsync(
            port,
            DenonAppCommand.GetAllZoneMuteStatus,
            cancellationToken).ConfigureAwait(false);
        var sourceXml = await QueryAppCommandAsync(
            port,
            DenonAppCommand.GetAllZoneSource,
            cancellationToken).ConfigureAwait(false);

        return DenonXmlParser.ParseSeparateAppCommandMainZoneStatus(
            powerXml,
            volumeXml,
            muteXml,
            sourceXml,
            availableInputs);
    }

    /// <summary>Tries the two documented HTTP ports, without assuming Deviceinfo and AppCommand share one.</summary>
    private async Task<int?> DiscoverAppCommandPortAsync(int deviceInfoPort, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { deviceInfoPort, deviceInfoPort == 80 ? 8080 : 80 })
        {
            if (await ProbeAppCommandAsync(candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }
        return null;
    }

    /// <summary>Probes the actual endpoint on a candidate port.</summary>
    private async Task<bool> ProbeAppCommandAsync(int port, CancellationToken cancellationToken)
    {
        // An optional endpoint must not block initialization for the full HTTP timeout.
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var xml = await QueryAppCommandAsync(
                    port, DenonAppCommand.GetAllZonePowerStatus, timeout.Token).ConfigureAwait(false);
                return DenonXmlParser.HasAppCommandData(xml);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                (exception is HttpRequestException || exception is OperationCanceledException ||
                 exception is DenonProtocolException || exception is System.Xml.XmlException))
            {
                return false;
            }
        }
    }

    private Task<string> QueryAppCommandAsync(
        int port,
        string command,
        CancellationToken cancellationToken) =>
        _httpTransport.PostXmlAsync(
            Host,
            AppCommandPort ?? port,
            DenonEndpoints.AppCommand,
            DenonAppCommand.CreateRequest(command),
            cancellationToken);

    private async Task<string?> TryQueryAppCommand0300Async(
        int port,
        string request,
        CancellationToken cancellationToken)
    {
        var preferred = AppCommand0300Port ?? AppCommandPort ?? port;
        var candidates = AppCommand0300Port.HasValue
            ? new[] { preferred }
            : new[] { preferred, preferred == 80 ? 8080 : 80 };
        foreach (var candidate in candidates)
        {
            try
            {
                var xml = await _httpTransport.PostXmlAsync(
                    Host, candidate, DenonEndpoints.AppCommand0300,
                    request, cancellationToken).ConfigureAwait(false);
                if (!DenonXmlParser.HasAppCommandData(xml)) continue;
                AppCommand0300Port = candidate;
                return xml;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                (exception is HttpRequestException || exception is DenonProtocolException ||
                 exception is System.Xml.XmlException || exception is OperationCanceledException))
            {
                // This optional endpoint is not available on this candidate port.
            }
        }
        return null;
    }

    /// <summary>Switches the Main Zone on using <see cref="PreferredControlProtocol"/>.</summary>
    public Task PowerOnAsync(CancellationToken cancellationToken = default) =>
        PowerOnAsync(PreferredControlProtocol, cancellationToken);

    /// <summary>Switches the Main Zone on through the selected transport.</summary>
    public Task PowerOnAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.MainZonePower, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.PowerOn, token),
            token => _telnetClient.PowerOnAsync(token),
            cancellationToken);

    /// <summary>Switches the Main Zone to standby using <see cref="PreferredControlProtocol"/>.</summary>
    public Task PowerOffAsync(CancellationToken cancellationToken = default) =>
        PowerOffAsync(PreferredControlProtocol, cancellationToken);

    /// <summary>Switches the Main Zone to standby through the selected transport.</summary>
    public Task PowerOffAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.MainZonePower, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.PowerStandby, token),
            token => _telnetClient.PowerOffAsync(token),
            cancellationToken);

    /// <summary>Raises the Main Zone volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task VolumeUpAsync(CancellationToken cancellationToken = default) =>
        VolumeUpAsync(PreferredControlProtocol, cancellationToken);


    /// <summary>Lowers the Main Zone volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task VolumeDownAsync(CancellationToken cancellationToken = default) =>
        VolumeDownAsync(PreferredControlProtocol, cancellationToken);


    /// <summary>Sets the Main Zone volume on Denon's absolute 0..98 scale.</summary>
    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SetVolumeAsync(volume, PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Main Zone volume on Denon's absolute 0..98 scale through the selected transport.</summary>
    public Task SetVolumeAsync(
        double volume,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        var normalizedVolume = DenonVolumeCodec.NormalizeVolume(volume);
        var httpValue = DenonVolumeCodec.ToHttpValue(normalizedVolume);
        return ExecuteControlAsync(AvrFeature.MainZoneVolume, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetVolume(httpValue), token),
            token => _telnetClient.SetVolumeAsync(normalizedVolume, token),
            cancellationToken);
    }

    /// <summary>Sets the Main Zone volume using the relative dB scale.</summary>
    public Task SetVolumeDbAsync(double volumeDb, CancellationToken cancellationToken = default) =>
        SetVolumeDbAsync(volumeDb, PreferredControlProtocol, cancellationToken);

    /// <summary>Sets the Main Zone volume in dB through the selected transport.</summary>
    public Task SetVolumeDbAsync(
        double volumeDb,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default) =>
        SetVolumeAsync(DenonVolumeCodec.FromDecibels(volumeDb), protocol, cancellationToken);

    /// <summary>Raises Zone 2 volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task Zone2VolumeUpAsync(CancellationToken cancellationToken = default) =>
        Zone2VolumeUpAsync(PreferredControlProtocol, cancellationToken);


    /// <summary>Lowers Zone 2 volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task Zone2VolumeDownAsync(CancellationToken cancellationToken = default) =>
        Zone2VolumeDownAsync(PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Zone 2 volume on Denon's absolute 0..98 scale.</summary>
    public Task SetZone2VolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SetZone2VolumeAsync(volume, PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Zone 2 volume on Denon's absolute 0..98 scale through the selected transport.</summary>
    public Task SetZone2VolumeAsync(
        double volume, DenonControlProtocol protocol, CancellationToken cancellationToken = default)
    {
        var normalizedVolume = DenonVolumeCodec.NormalizeVolume(volume);
        var httpValue = DenonVolumeCodec.ToHttpValue(normalizedVolume);
        return ExecuteControlAsync(AvrFeature.Zone2Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetZoneVolume(2, httpValue), token),
            token => _telnetClient.SetZone2VolumeAsync(normalizedVolume, token),
            cancellationToken);
    }

    /// <summary>Sets Zone 2 volume using the relative dB scale.</summary>
    public Task SetZone2VolumeDbAsync(double volumeDb, CancellationToken cancellationToken = default) =>
        SetZone2VolumeDbAsync(volumeDb, PreferredControlProtocol, cancellationToken);

    public Task SetZone2VolumeDbAsync(
        double volumeDb, DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        SetZone2VolumeAsync(DenonVolumeCodec.FromDecibels(volumeDb), protocol, cancellationToken);

    /// <summary>Sets Zone 2 muting. Mute is kept separate from the volume value.</summary>
    public Task SetZone2MuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SetZone2MuteAsync(muted, PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Zone 2 muting through the selected transport.</summary>
    public Task SetZone2MuteAsync(
        bool muted,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone2Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetZoneMute(2, muted), token),
            token => _telnetClient.SetZone2MuteAsync(muted, token),
            cancellationToken);

    /// <summary>Raises Zone 3 volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task Zone3VolumeUpAsync(CancellationToken cancellationToken = default) =>
        Zone3VolumeUpAsync(PreferredControlProtocol, cancellationToken);

    /// <summary>Lowers Zone 3 volume by one receiver step using <see cref="PreferredControlProtocol"/>.</summary>
    public Task Zone3VolumeDownAsync(CancellationToken cancellationToken = default) =>
        Zone3VolumeDownAsync(PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Zone 3 volume on Denon's absolute 0..98 scale.</summary>
    public Task SetZone3VolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        SetZone3VolumeAsync(volume, PreferredControlProtocol, cancellationToken);

    /// <summary>Sets Zone 3 volume on Denon's absolute 0..98 scale through the selected transport.</summary>
    public Task SetZone3VolumeAsync(
        double volume, DenonControlProtocol protocol, CancellationToken cancellationToken = default)
    {
        var normalizedVolume = DenonVolumeCodec.NormalizeVolume(volume);
        var httpValue = DenonVolumeCodec.ToHttpValue(normalizedVolume);
        return ExecuteControlAsync(AvrFeature.Zone3Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetZoneVolume(3, httpValue), token),
            token => _telnetClient.SetZone3VolumeAsync(normalizedVolume, token),
            cancellationToken);
    }

    /// <summary>Sets Zone 3 volume using the relative dB scale.</summary>
    public Task SetZone3VolumeDbAsync(double volumeDb, CancellationToken cancellationToken = default) =>
        SetZone3VolumeDbAsync(volumeDb, PreferredControlProtocol, cancellationToken);

    public Task SetZone3VolumeDbAsync(
        double volumeDb, DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        SetZone3VolumeAsync(DenonVolumeCodec.FromDecibels(volumeDb), protocol, cancellationToken);

    /// <summary>Sets Zone 3 muting. Mute is kept separate from the volume value.</summary>
    public Task SetZone3MuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SetZone3MuteAsync(muted, PreferredControlProtocol, cancellationToken);

    /// <summary>Enables or disables Main Zone muting.</summary>
    /// <param name="muted"><see langword="true"/> to mute; otherwise <see langword="false"/>.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        SetMuteAsync(muted, PreferredControlProtocol, cancellationToken);

    /// <summary>Enables or disables Main Zone muting through the selected transport.</summary>
    public Task SetMuteAsync(
        bool muted,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.MainZoneMute, protocol,
            token => SendHttpCommandAsync(muted ? DenonEndpoints.MuteOn : DenonEndpoints.MuteOff, token),
            token => _telnetClient.SetMuteAsync(muted, token),
            cancellationToken);

    /// <summary>Selects a Main Zone input using a display or Denon protocol name.</summary>
    /// <param name="input">Input such as <c>CBL/SAT</c>, <c>Media Player</c> or <c>MPLAY</c>.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public Task SetInputAsync(string input, CancellationToken cancellationToken = default) =>
        SetInputAsync(input, PreferredControlProtocol, cancellationToken);

    /// <summary>Selects a Main Zone input through the selected transport.</summary>
    public Task SetInputAsync(
        string input,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(input, nameof(input));

        if (input.Contains('\r') || input.Contains('\n'))
        {
            throw new ArgumentException("Der Eingangsname darf keinen Zeilenumbruch enthalten.", nameof(input));
        }

        var protocolName = DenonInputSource.ToProtocolName(input.Trim());
        return ExecuteControlAsync(AvrFeature.MainZoneInput, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetInput(protocolName), token),
            token => _telnetClient.SetInputAsync(input, token),
            cancellationToken);
    }

    /// <summary>Reads one speaker channel level through Telnet.</summary>
    public Task<DenonSpeakerLevel> GetSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default) =>
        ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelRead,
            protocol,
            token => _telnetClient.GetSpeakerLevelAsync(channel, token),
            cancellationToken);

    /// <summary>Reads all currently configured speaker channel levels through Telnet.</summary>
    public Task<IReadOnlyList<DenonSpeakerLevel>> GetSpeakerLevelsAsync(
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default) =>
        ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelRead,
            protocol,
            _telnetClient.GetSpeakerLevelsAsync,
            cancellationToken);

    /// <summary>Sets one speaker channel level through Telnet.</summary>
    public async Task SetSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        double decibels,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelControl,
            protocol,
            token => _telnetClient.SetSpeakerLevelAsync(channel, decibels, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Raises or lowers one speaker channel level through Telnet.</summary>
    public async Task ChangeSpeakerLevelAsync(
        DenonSpeakerLevelChannel channel,
        bool increase,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelControl,
            protocol,
            token => _telnetClient.ChangeSpeakerLevelAsync(channel, increase, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Switches one subwoofer channel level off through Telnet.</summary>
    public async Task SetSpeakerLevelOffAsync(
        DenonSpeakerLevelChannel channel,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelControl,
            protocol,
            token => _telnetClient.SetSpeakerLevelOffAsync(channel, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets a speaker-preset level through the receiver-specific speaker configuration API.
    /// The index is the <c>Speaker index</c> used by that interface; values use 0.1 dB units.
    /// </summary>
    public Task SetSpeakerPresetLevelAsync(
            int speakerIndex,
            double decibels,
            CancellationToken cancellationToken = default)
    {
        if (speakerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speakerIndex));
        }

        if (decibels is < -SpeakerLevelLimitDecibels or > SpeakerLevelLimitDecibels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decibels),
                FormattableString.Invariant(
                    $"The level must be between {-SpeakerLevelLimitDecibels:+0.0;-0.0} and {SpeakerLevelLimitDecibels:+0.0;-0.0} dB."));
            // <-----------
        }

        return GetSpeakerTransportProfile().SpeakerPresetLevels.SetLevelAsync(
            new DenonProfileContext(Host, _httpTransport),
            speakerIndex,
            decibels,
            cancellationToken);
    }

    /// <summary>
    /// Reads the native speaker indices from each AJAX page (levels, distances, menu,
    /// configuration and layout). This does not change receiver settings and does not
    /// equate indices from different pages. Unsupported optional pages produce warnings.
    /// Profile mappings are returned as hints only, never as receiver-confirmed labels.
    /// </summary>
    public Task<DenonSpeakerDiscoveryReport> DiscoverSpeakerIndicesAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = GetSpeakerTransportProfile();
        var options = profile.SpeakerDiscoveryOptions
            ?? throw new NotSupportedException(
                $"Speaker AJAX discovery is not configured for profile '{profile.Id}'.");
        return DenonSpeakerDiscoveryReader.ReadAsync(
            new DenonProfileContext(Host, _httpTransport),
            options,
            profile.Id,
            cancellationToken);
    }

    /// <summary>
    /// Diagnoses documented, read-only Legacy HTTP and AppCommand endpoints on
    /// the detected HTTP port. This does not depend on an X4100W model profile.
    /// No links discovered inside the web UI are followed automatically.
    /// </summary>
    public Task<DenonLegacyHttpDiscoveryReport> DiscoverLegacyHttpAsync(
        CancellationToken cancellationToken = default) =>
        DenonLegacyHttpDiscoveryReader.ReadAsync(
            Host, GetInitializedPort(), _httpTransport, cancellationToken);

    /// <summary>
    /// Reads native GetActiveSpeaker matrix entries through AppCommand0300 without
    /// requiring a model speaker profile. The reported slots/control codes are
    /// not calibration-level or distance indices. An empty result does not mean
    /// the receiver has no configured speakers.
    /// </summary>
    public async Task<DenonSpeakerMatrixDiscovery> DiscoverSpeakerMatrixAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var port = GetInitializedPort();
        var xml = await TryQueryAppCommand0300Async(port,
            DenonAppCommand.CreateDetailedRequest(DenonAppCommand.GetActiveSpeaker, "activespall"),
            cancellationToken).ConfigureAwait(false);
        return DenonSpeakerMatrixDiscoveryReader.Parse(xml,
            xml == null ? (int?)null : AppCommand0300Port);
    }

    /// <summary>
    /// Re-reads the full Deviceinfo capability catalog independently of model profiles.
    /// The resulting metadata is advertised, not necessarily runtime-verifiable.
    /// No writes are sent. The raw XML, MAC address and host are not stored here.
    /// </summary>
    public async Task<DenonDeviceCapabilityDiscovery> RefreshDeviceCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var port = GetInitializedPort();
        var xml = await _httpTransport.GetStringAsync(
            Host, port, DenonEndpoints.DeviceInfo, cancellationToken).ConfigureAwait(false);
        var discovered = DenonDeviceCapabilityReader.Parse(xml, port);
        DeviceCapabilities = discovered;
        return discovered;
    }

    /// <summary>
    /// Tests two advertised read commands on both XML protocol families. A response
    /// whose structure is recognized is NOT proof that all returned values are
    /// semantically usable; these probes never send Set commands.
    /// </summary>
    public async Task<IReadOnlyList<DenonReadCommandProbe>> ProbeAdvertisedReadCommandsAsync(
        CancellationToken cancellationToken = default)
    {
        var port = GetInitializedPort();
        var capabilities = DeviceCapabilities ??
            await RefreshDeviceCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        var probes = new List<DenonReadCommandProbe>();
        foreach (var command in new[] { "GetChLevel", "GetSoundModeList" })
        {
            if (!capabilities.IsCommandAdvertised(command))
            {
                probes.Add(new DenonReadCommandProbe(command, "Deviceinfo.xml", port,
                    "NotAdvertised", Array.Empty<string>(), "No enabled declaration in Deviceinfo.xml."));
                continue;
            }

            // The Deviceinfo file advertises the command names, but does not
            // reliably specify which AppCommand wire envelope each needs.
            // Probe both read-only encodings, without guessing writable commands.
            foreach (var detailed in new[] { false, true })
            {
                var endpoint = detailed ? DenonEndpoints.AppCommand0300 : DenonEndpoints.AppCommand;
                var candidatePort = detailed ? AppCommand0300Port ?? AppCommandPort ?? port
                                             : AppCommandPort ?? port;
                var request = detailed ? DenonAppCommand.CreateDetailedRequest(command)
                                       : DenonAppCommand.CreateRequest(command);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        var response = await _httpTransport.PostXmlAsync(Host, candidatePort,
                            endpoint, request, timeout.Token).ConfigureAwait(false);
                        probes.Add(DenonReadCommandInspector.Inspect(command, endpoint,
                            candidatePort, response));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is HttpRequestException ||
                        exception is OperationCanceledException || exception is InvalidDataException ||
                        exception is System.Xml.XmlException || exception is IOException)
                    {
                        probes.Add(new DenonReadCommandProbe(command, endpoint, candidatePort,
                            "Error", Array.Empty<string>(), exception is OperationCanceledException
                                ? "Probe timed out" : exception.GetType().Name));
                    }
                }
            }
        }
        return probes;
    }

    /// <summary>
    /// Reads the optional, per-input Channel Level Adjust menu through the legacy
    /// AppCommand interface. These are NOT the Audyssey/manual speaker calibration levels.
    /// The original receiver field values are returned without guessing their units.
    /// </summary>
    public async Task<IReadOnlyList<DenonChannelLevelAdjustment>> GetChannelLevelAdjustmentsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var port = AppCommandPort ?? GetInitializedPort();
        var response = await _httpTransport.PostXmlAsync(Host, port,
            DenonEndpoints.AppCommand, DenonAppCommand.CreateRequest("GetChLevel"),
            cancellationToken).ConfigureAwait(false);
        return DenonChannelLevelAdjustmentReader.Parse(response);
    }

    /// <summary>
    /// Selects Movie, Music, Game, Direct or Pure Direct via the receiver's
    /// native Telnet sound-mode family commands. The command changes AVR state.
    /// Deviceinfo advertisements are hints, not guaranteed runtime availability.
    /// </summary>
    public async Task<string> SelectSoundModeFamilyAsync(DenonSoundModeFamily family,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (ReceiverCapabilities?.SupportsTelnet == false)
            throw new NotSupportedException("Telnet is not available for this receiver.");
        var selected = await SelectSoundModeFamilyAndReadBackAsync(family, cancellationToken)
            .ConfigureAwait(false);
        if (!selected.HasModeStatus)
            throw new IOException("Sound mode family command was sent, but MS? could not verify the current mode.");
        return selected.CurrentModeResponse;
    }

    /// <summary>
    /// Reads the optional native dynamic Movie/Music/Game/Pure list; a timeout
    /// means that this protocol is not verified, not that the AVR has no modes.
    /// </summary>
    public Task<IReadOnlyList<DenonSoundModeOption>> GetSoundModeOptionsAsync(
        CancellationToken cancellationToken = default) =>
        _telnetClient.GetSoundModeOptionsAsync(cancellationToken);

    /// <summary>
    /// Reads persistent calibration levels and distances directly from a legacy
    /// Denon Speakers/Levels and Speakers/Distances web setup, using only HTTP GET.
    /// Works by checking the actual pages, not by assuming a model profile.
    /// Does not use per-input GetChLevel, Telnet CV?, or modern AJAX preset indices.
    /// Both pages must be available; otherwise this method fails explicitly.
    /// </summary>
    public Task<DenonLegacySpeakerCalibrationSnapshot> GetLegacySpeakerCalibrationAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return DenonLegacySpeakerCalibrationReader.ReadAsync(
            Host, GetInitializedPort(), _httpTransport, cancellationToken);
    }

    /// <summary>Reads persistent legacy speaker setup calibration levels (signed dB) through HTTP GET.</summary>
    public Task<IReadOnlyList<DenonLegacySpeakerCalibrationLevel>> GetLegacySpeakerCalibrationLevelsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return DenonLegacySpeakerCalibrationReader.ReadLevelsAsync(
            Host, GetInitializedPort(), _httpTransport, cancellationToken);
    }

    /// <summary>Reads persistent legacy speaker setup distances, normalized to meters, through HTTP GET.</summary>
    public async Task<IReadOnlyList<DenonLegacySpeakerCalibrationDistance>> GetLegacySpeakerCalibrationDistancesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var page = await DenonLegacySpeakerCalibrationReader.ReadDistancesAsync(
            Host, GetInitializedPort(), _httpTransport, cancellationToken).ConfigureAwait(false);
        return page.Speakers;
    }

    /// <summary>
    /// Reads persistent calibration through the correct receiver family without
    /// forcing consumers to handle legacy HTML vs modern speaker AJAX.
    /// The snapshot is a read-only representation and is NOT the per-input CV trim.
    /// </summary>
    public async Task<DenonSpeakerCalibrationSnapshot> GetSpeakerCalibrationAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var profile = GetSpeakerTransportProfile();
        if (profile.SupportsSpeakerPresetLevels && profile.SupportsSpeakerDistances)
        {
            var result = await ReadModernCalibrationAsync(profile, cancellationToken)
                .ConfigureAwait(false);
            _verifiedSpeakerProfile = profile;
            return result;
        }

        // Only unknown 0301 receivers are probed for additional modern transports.
        // A recognized legacy model uses its actual HTML setup pages directly.
        if (profile.Id == "unknown")
        {
            var discovered = await TryDiscoverModernCalibrationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (discovered is not null)
                return discovered;
        }

        var channels = new Dictionary<string, DenonCalibrationChannel>(StringComparer.OrdinalIgnoreCase);
        var legacy = await GetLegacySpeakerCalibrationAsync(cancellationToken).ConfigureAwait(false);
        foreach (var level in legacy.Levels)
            channels.Add(level.NativeChannelCode, new DenonCalibrationChannel(
                level.NativeChannelCode, level.DisplayName, level.Decibels, null));
        foreach (var distance in legacy.Distances)
        {
            if (channels.TryGetValue(distance.NativeChannelCode, out var value))
                channels[distance.NativeChannelCode] = value with { DistanceMeters = distance.Meters };
            else channels.Add(distance.NativeChannelCode, new DenonCalibrationChannel(
                distance.NativeChannelCode, distance.DisplayName, null, distance.Meters));
        }
        return new DenonSpeakerCalibrationSnapshot("Legacy speaker HTML", DateTimeOffset.UtcNow,
            channels.Values.OrderBy(x => x.NativeChannelCode).ToArray());
    }

    private async Task<DenonSpeakerCalibrationSnapshot> ReadModernCalibrationAsync(
        IDenonReceiverProfile profile, CancellationToken cancellationToken)
    {
        var context = new DenonProfileContext(Host, _httpTransport);
        var levels = await profile.SpeakerPresetLevels.GetLevelsAsync(context, cancellationToken)
            .ConfigureAwait(false);
        var distances = await profile.SpeakerDistances.GetDistancesAsync(context, cancellationToken)
            .ConfigureAwait(false);
        var channels = new Dictionary<string, DenonCalibrationChannel>(StringComparer.OrdinalIgnoreCase);
        foreach (var level in levels)
        {
            // A multi-bit channel is a GROUP control (e.g. SW+SW2), not a speaker.
            // It is still available in GetSpeakerPresetLevelsAsync for diagnostics.
            if (level.Channel.HasValue && !IsSingleChannel(level.Channel.Value))
                continue;
            var native = level.Channel.HasValue
                ? ChannelName(level.Channel.Value) : "LEVEL#" + level.SpeakerIndex;
            channels[native] = new DenonCalibrationChannel(native, native,
                level.Decibels, null, level.SpeakerIndex);
        }
        foreach (var distance in distances.Speakers)
        {
            if (distance.Channel.HasValue && !IsSingleChannel(distance.Channel.Value))
                continue;
            var native = distance.Channel.HasValue
                ? ChannelName(distance.Channel.Value) : "DISTANCE#" + distance.SpeakerIndex;
            if (channels.TryGetValue(native, out var value))
                channels[native] = value with { DistanceMeters = distance.Meters,
                    DistanceIndex = distance.SpeakerIndex };
            else
                channels[native] = new DenonCalibrationChannel(native, native, null,
                    distance.Meters, null, distance.SpeakerIndex);
        }
        if (channels.Count == 0)
            throw new InvalidDataException("Modern speaker AJAX did not return usable calibration data.");
        return new DenonSpeakerCalibrationSnapshot("Modern speaker AJAX", DateTimeOffset.UtcNow,
            channels.Values.OrderBy(x => x.NativeChannelCode).ToArray());
    }

    private static bool IsSingleChannel(SpeakerChannel channel) =>
        DenonReceiverProfileRegistry.IsPhysicalSpeakerChannel(channel);

    private static string ChannelName(SpeakerChannel channel) =>
        SpeakerChannelParser.ToNativeCode(channel);

    /// <summary>
    /// Sends a family-button command and then reads MS? to expose the actual mode.
    /// For example MSGAME may legitimately resolve to MSDTS NEO:X G.
    /// </summary>
    public async Task<DenonSoundModeSelectionResult> SelectSoundModeFamilyAndReadBackAsync(
        DenonSoundModeFamily family, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (ReceiverCapabilities?.SupportsTelnet == false)
            throw new NotSupportedException("Telnet is not available for this receiver.");

        // The AVC-X6700H may perform MSMOVIE/MSMUSIC/MSGAME without echoing MS.
        // Observe SSSMG independently and verify via MS?; do not require a SET echo.
        string previous = string.Empty;
        try { previous = await _telnetClient.QuerySurroundModeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            (ex is IOException || ex is OperationCanceledException)) { }

        var session = DenonTelnetSession.ForHost(Host);
        string? familyEvent = null;
        Action<DenonTelnetEvent> onLine = received =>
        {
            if (DenonSoundModeFamilyVerifier.IsMatchingFamilyNotification(family, received.Message))
                Volatile.Write(ref familyEvent, received.Message);
        };
        session.LineReceived += onLine;
        string actual = string.Empty;
        try
        {
            await _telnetClient.SendSoundModeFamilyWithoutAckAsync(family, cancellationToken)
                .ConfigureAwait(false);
            // Let receivers with asynchronous family selection settle before asking.
            await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reply = await _telnetClient.QuerySurroundModeAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (DenonSoundModeFamilyVerifier.IsModeStatus(reply)) actual = reply;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                    (ex is IOException || ex is OperationCanceledException)) { }

                var grade = DenonSoundModeFamilyVerifier.Evaluate(
                    family, previous, actual, Volatile.Read(ref familyEvent) is not null);
                if (grade == DenonSoundModeVerification.Confirmed) break;
                if (attempt < 5) await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { session.LineReceived -= onLine; }

        var notification = Volatile.Read(ref familyEvent);
        return new DenonSoundModeSelectionResult(family,
            DenonSoundModeFamilyVerifier.GetCommand(family) + " (sent; no SET acknowledgement assumed)",
            actual)
        {
            PreviousModeResponse = previous,
            FamilyNotification = notification,
            Verification = DenonSoundModeFamilyVerifier.Evaluate(
                family, previous, actual, notification is not null)
        };
    }

    /// <summary>
    /// Receiver-advertised Main Zone mode catalog. This is NOT a signal-dependent
    /// Movie/Music/Game submenu; unlike OPSMLALL it works on legacy receivers.
    /// </summary>
    public IReadOnlyList<DenonAdvertisedSoundMode> GetAdvertisedSoundModeCatalog() =>
        DeviceCapabilities?.SoundModes ?? Array.Empty<DenonAdvertisedSoundMode>();

    /// <summary>
    /// Changes one persistent calibration LEVEL. On legacy receivers the write is
    /// experimental and opt-in. The web form is rebuilt from a fresh GET; after
    /// submission a second GET MUST confirm the requested value.
    /// </summary>
    public async Task SetCalibrationLevelAsync(string nativeChannelCode, double decibels,
        bool allowExperimentalLegacyWrite = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(nativeChannelCode, nameof(nativeChannelCode));
        if (double.IsNaN(decibels) || double.IsInfinity(decibels) ||
            decibels < -12 || decibels > 12 || Math.Abs(decibels * 2 - Math.Round(decibels * 2)) > 0.000001)
            throw new ArgumentOutOfRangeException(nameof(decibels));
        // Resolve/verify the actual calibration transport before deciding whether a legacy POST is allowed.
        var baseline = await GetSpeakerCalibrationAsync(cancellationToken).ConfigureAwait(false);
        var profile = GetSpeakerTransportProfile();
        if (profile.SupportsSpeakerPresetLevels && profile.SupportsSpeakerDistances)
        {
            var channel = baseline.Channels.SingleOrDefault(x =>
                string.Equals(x.NativeChannelCode, nativeChannelCode, StringComparison.OrdinalIgnoreCase));
            if (channel is null || channel.LevelIndex is null)
                throw new ArgumentException("Unknown persistent level channel or index.", nameof(nativeChannelCode));
            await SetSpeakerPresetLevelAsync(channel.LevelIndex.Value, decibels, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (!allowExperimentalLegacyWrite)
                throw new NotSupportedException("Legacy speaker form writes require explicit opt-in and a backup.");
            var port = GetInitializedPort();
            var path = DenonLegacySpeakerCalibrationReader.LevelsPath;
            var originalHtml = await _httpTransport.GetStringAsync(Host, port, path, cancellationToken)
                .ConfigureAwait(false);
            var form = DenonLegacySpeakerCalibrationReader.BuildLevelForm(originalHtml,
                nativeChannelCode, decibels);
            _ = await _httpTransport.PostFormAsync(Host, port,
                "/SETUP/SPEAKERS/LEVELS/s_speakersetup.asp", form, cancellationToken).ConfigureAwait(false);
        }
        if (!await VerifyCalibrationReadbackAsync(nativeChannelCode, decibels,
                isDistance: false, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Level write attempted, but target value was NOT confirmed after three readbacks. " +
                "The AVR may still have changed; read the current value before retrying.");
    }

    /// <summary>Changes a persistent distance in meters, with explicit legacy opt-in and readback.</summary>
    public async Task SetCalibrationDistanceAsync(string nativeChannelCode, double meters,
        bool allowExperimentalLegacyWrite = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(nativeChannelCode, nameof(nativeChannelCode));
        if (double.IsNaN(meters) || double.IsInfinity(meters) || meters <= 0 || meters > 18)
            throw new ArgumentOutOfRangeException(nameof(meters));
        // Resolve/verify the actual calibration transport before deciding whether a legacy POST is allowed.
        var baseline = await GetSpeakerCalibrationAsync(cancellationToken).ConfigureAwait(false);
        var profile = GetSpeakerTransportProfile();
        if (profile.SupportsSpeakerPresetLevels && profile.SupportsSpeakerDistances)
        {
            var channel = baseline.Channels.SingleOrDefault(x =>
                string.Equals(x.NativeChannelCode, nativeChannelCode, StringComparison.OrdinalIgnoreCase));
            if (channel is null || channel.DistanceIndex is null)
                throw new ArgumentException("Unknown persistent distance channel or index.", nameof(nativeChannelCode));
            await SetSpeakerDistanceAsync(channel.DistanceIndex.Value, meters, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (!allowExperimentalLegacyWrite)
                throw new NotSupportedException("Legacy distance form writes require explicit opt-in and a backup.");
            var port = GetInitializedPort();
            var path = DenonLegacySpeakerCalibrationReader.DistancesPath;
            var originalHtml = await _httpTransport.GetStringAsync(Host, port, path, cancellationToken)
                .ConfigureAwait(false);
            var form = DenonLegacySpeakerCalibrationReader.BuildDistanceForm(originalHtml,
                nativeChannelCode, meters);
            _ = await _httpTransport.PostFormAsync(Host, port,
                "/SETUP/SPEAKERS/DISTANCES/s_speakersetup.asp", form, cancellationToken)
                .ConfigureAwait(false);
        }
        if (!await VerifyCalibrationReadbackAsync(nativeChannelCode, meters,
                isDistance: true, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Distance write attempted, but target value was NOT confirmed after three readbacks. " +
                "The AVR may still have changed; read the current value before retrying.");
    }

    /// <summary>
    /// Re-read the actual stored value up to three times. A POST/HTTP 200 alone
    /// is not evidence that the receiver accepted a calibration change.
    /// </summary>
    private async Task<bool> VerifyCalibrationReadbackAsync(string nativeChannelCode,
        double expected, bool isDistance, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0)
                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
            try
            {
                var snapshot = await GetSpeakerCalibrationAsync(cancellationToken).ConfigureAwait(false);
                var found = snapshot.Channels.SingleOrDefault(x =>
                    string.Equals(x.NativeChannelCode, nativeChannelCode,
                        StringComparison.OrdinalIgnoreCase));
                var value = isDistance ? found?.DistanceMeters : found?.LevelDecibels;
                if (value.HasValue && Math.Abs(value.Value - expected) <=
                    (isDistance ? 0.035 : 0.01))
                    return true;
            }
            catch (Exception ex) when (attempt < 2 &&
                (ex is System.Net.Http.HttpRequestException || ex is IOException ||
                 ex is InvalidDataException || ex is TimeoutException))
            {
                // A legacy receiver can be briefly unavailable while saving.
            }
        }
        return false;
    }

    /// <summary>Reads the active speaker-preset levels using the selected receiver profile.</summary>
    public Task<IReadOnlyList<DenonSpeakerPresetLevel>> GetSpeakerPresetLevelsAsync(
        CancellationToken cancellationToken = default) =>
        GetSpeakerTransportProfile().SpeakerPresetLevels.GetLevelsAsync(
            new DenonProfileContext(Host, _httpTransport),
            cancellationToken);

    /// <summary>
    /// Captures the current persistent speaker-preset levels and returns an immutable snapshot.
    /// </summary>
    public async Task<DenonSpeakerLevelSnapshot> CreateSpeakerLevelSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = GetSpeakerTransportProfile();

        var levels = await profile.SpeakerPresetLevels
            .GetLevelsAsync(
                new DenonProfileContext(
                    Host,
                    _httpTransport),
                cancellationToken)
            .ConfigureAwait(false);

        return new DenonSpeakerLevelSnapshot(
            DateTimeOffset.UtcNow,
            profile.Id,
            levels.ToArray());
    }

    /// <summary>
    /// Captures the current persistent speaker-preset levels in the client's convenience snapshot slot.
    /// </summary>
    public async Task SaveSpeakerLevelSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        _speakerLevelSnapshot =
            await CreateSpeakerLevelSnapshotAsync(
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Restores every level stored in a previously captured speaker-level snapshot.
    /// </summary>
    public async Task RestoreSpeakerLevelSnapshotAsync(
        DenonSpeakerLevelSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNull(snapshot, nameof(snapshot));

        var profile = GetSpeakerTransportProfile();

        if (!string.Equals(
            snapshot.ReceiverProfileId,
            profile.Id,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Der Snapshot wurde fuer Receiver-Profil '{snapshot.ReceiverProfileId}' erstellt, " +
                $"der aktuelle Receiver verwendet aber '{profile.Id}'.");
        }

        var context =
            new DenonProfileContext(
                Host,
                _httpTransport);

        foreach (var level in snapshot.Levels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await profile.SpeakerPresetLevels
                .SetLevelAsync(
                    context,
                    level.SpeakerIndex,
                    level.Decibels,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Restores the snapshot previously stored with <see cref="SaveSpeakerLevelSnapshotAsync"/>.
    /// </summary>
    public Task RestoreSpeakerLevelSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            _speakerLevelSnapshot
            ?? throw new InvalidOperationException(
                "Es wurde noch kein Speaker-Level-Snapshot gespeichert.");

        return RestoreSpeakerLevelSnapshotAsync(
            snapshot,
            cancellationToken);
    }

    /// <summary>
    /// Reads the persistent speaker distances of the active speaker preset.
    /// Returned values are always in meters.
    /// </summary>
    public Task<DenonSpeakerDistanceConfiguration> GetSpeakerDistancesAsync(
        CancellationToken cancellationToken = default) =>
        GetSpeakerTransportProfile()
            .SpeakerDistances
            .GetDistancesAsync(
                new DenonProfileContext(
                    Host,
                    _httpTransport),
                cancellationToken);

    /// <summary>
    /// Sets one persistent speaker distance in meters using the receiver-specific speaker setup API.
    /// </summary>
    public Task SetSpeakerDistanceAsync(
        int speakerIndex,
        double meters,
        CancellationToken cancellationToken = default)
    {
        if (speakerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speakerIndex));
        }

        if (double.IsNaN(meters) ||
            double.IsInfinity(meters) ||
            meters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(meters),
                "Die Distanz muss groesser als 0 Meter sein.");
            // <-----------
        }

        return GetSpeakerTransportProfile()
            .SpeakerDistances
            .SetDistanceAsync(
                new DenonProfileContext(
                    Host,
                    _httpTransport),
                speakerIndex,
                meters,
                cancellationToken);
    }

    /// <summary>
    /// Returns the speakers that exist in the receiver's persistent speaker configuration together with
    /// their level (dB) and distance (meters) from the active speaker preset (HTTP only).
    /// The speaker setup pages themselves are authoritative for configured speakers; this method therefore
    /// does not depend on the currently active audio channels reported by <c>GetActiveSpeaker</c>.
    /// Level and distance pages use different index schemes and are joined by channel. Combined group
    /// entries (for example the aggregate subwoofer entry) are omitted.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has not been initialized.</exception>
    /// <exception cref="NotSupportedException">The receiver profile does not support levels or distances.</exception>
    public async Task<IReadOnlyList<DenonConfiguredSpeaker>> GetConfiguredSpeakersAsync(
    CancellationToken cancellationToken = default)
    {
        var profile = GetSpeakerTransportProfile();

        if (!profile.SupportsSpeakerPresetLevels || !profile.SupportsSpeakerDistances)
        {
            throw new NotSupportedException(
                "Das Lesen von Lautsprecher-Pegeln und -Distanzen wird von diesem Receiver nicht unterstützt.");
            // <-----------
        }

        var context = new DenonProfileContext(Host, _httpTransport);
        var levels = await profile.SpeakerPresetLevels
            .GetLevelsAsync(context, cancellationToken)
            .ConfigureAwait(false);
        var distanceConfiguration = await profile.SpeakerDistances
            .GetDistancesAsync(context, cancellationToken)
            .ConfigureAwait(false);

        // Level and distance pages use different index schemes, so they are joined by channel.
        var levelsByChannel = levels
            .Where(level => level.Channel is not null)
            .ToLookup(level => level.Channel!.Value);
        var distancesByChannel = distanceConfiguration.Speakers
            .Where(distance => distance.Channel is not null)
            .ToLookup(distance => distance.Channel!.Value);

        var channels = levelsByChannel
            .Select(group => group.Key)
            .Union(distancesByChannel.Select(group => group.Key))
            .ToArray();

        List<DenonConfiguredSpeaker> result = [];

        foreach (var channel in channels)
        {
            // Combined group entry (for example 32 or 35): not a physical speaker.
            if (channel == SpeakerChannel.None ||
                NetFrameworkCompat.BitOperations.PopCount((ulong)channel) != 1)
            {
                continue;
                // <-----------
            }

            var level = levelsByChannel[channel].FirstOrDefault();
            var distance = distancesByChannel[channel].FirstOrDefault();

            result.Add(new DenonConfiguredSpeaker(
                level?.SpeakerIndex ?? distance!.SpeakerIndex,
                channel,
                level?.Decibels,
                distance?.Meters));
        }

        result.Sort((left, right) => left.SpeakerIndex.CompareTo(right.SpeakerIndex));

        return result;
        // <-----------
    }


    /// <summary>Sends a complete Denon HTTP command path.</summary>
    /// <param name="commandPath">Path beginning with <c>/</c>, including any query command.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task SendCommandAsync(
        string commandPath,
        CancellationToken cancellationToken = default)
    {
        NetFrameworkCompat.ThrowIfNullOrWhiteSpace(commandPath, nameof(commandPath));
        await SendHttpCommandAsync(commandPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsTransport)
        {
            _httpTransport.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Reads the number of the currently active speaker preset through the receiver-specific
    /// speaker setup web API (HTTP only).
    /// </summary>
    public Task<int> GetActiveSpeakerPresetAsync(
        CancellationToken cancellationToken = default) =>
        GetSpeakerTransportProfile()
            .SpeakerPresetSelection
            .GetActivePresetAsync(
                new DenonProfileContext(
                    Host,
                    _httpTransport),
                cancellationToken);

    /// <summary>
    /// Switches the active speaker preset through the receiver-specific speaker setup web API (HTTP only)
    /// and returns once the receiver confirms the new preset. A switch can take several seconds.
    /// Calls must not overlap with other calls on the same client.
    /// </summary>
    /// <param name="preset">Speaker preset number, starting at 1.</param>
    /// <param name="confirmationTimeout">
    /// Maximum time to wait for the confirmation after the switch was requested; defaults to 30 seconds.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="DenonProtocolException">The receiver did not confirm the switch in time.</exception>
    public Task SelectSpeakerPresetAsync(
        int preset,
        TimeSpan? confirmationTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var profile = GetSpeakerTransportProfile();

        // Unsupported profiles report NotSupportedException from their provider instead.
        if (profile.SupportsSpeakerPresetSelection &&
            (preset < 1 || preset > profile.SpeakerPresetCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset),
                preset,
                $"Das Speaker Preset muss zwischen 1 und {profile.SpeakerPresetCount} liegen.");
            // <-----------
        }

        if (confirmationTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationTimeout));
            // <-----------
        }

        return profile
            .SpeakerPresetSelection
            .SelectPresetAsync(
                new DenonProfileContext(
                    Host,
                    _httpTransport),
                preset,
                confirmationTimeout ?? DefaultSpeakerPresetConfirmationTimeout,
                cancellationToken);
        // <-----------
    }


    private int GetInitializedPort()
    {
        ThrowIfDisposed();
        return HttpPort ?? throw new InvalidOperationException(
            "InitializeAsync muss vor dem Senden von Befehlen aufgerufen werden.");
    }

    private void ThrowIfDisposed() => NetFrameworkCompat.ThrowIfDisposed(_disposed, this);

    private async Task SendHttpCommandAsync(string commandPath, CancellationToken cancellationToken)
    {
        var port = GetInitializedPort();
        _ = await _httpTransport.GetStringAsync(Host, port, commandPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task<T> ExecuteTelnetFeatureAsync<T>(
        AvrFeature feature,
        DenonControlProtocol protocol,
        Func<CancellationToken, Task<T>> telnetCommand,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        EnsureProtocolSupportsFeature(
            DenonControlProtocol.Telnet,
            feature,
            GetSupportedProtocols(feature));

        if (protocol == DenonControlProtocol.Http)
        {
            throw new NotSupportedException($"{feature} wird über HTTP von DenonAvrNet nicht unterstützt.");
        }

        if (protocol is not (DenonControlProtocol.Auto or DenonControlProtocol.Telnet))
        {
            throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null);
        }

        return telnetCommand(cancellationToken);
    }

    private static void EnsureProtocolSupportsFeature(
        DenonControlProtocol protocol,
        AvrFeature feature,
        ISet<DenonControlProtocol> supportedProtocols)
    {
        if (!supportedProtocols.Contains(protocol))
        {
            throw new NotSupportedException(
                $"{feature} wird über {protocol} von DenonAvrNet nicht unterstützt.");
        }
    }

    private static DenonReceiverCapabilities CreateReceiverCapabilities(
        DenonDeviceInfo deviceInfo,
        bool supportsAppCommand)
    {
        var zoneCount = deviceInfo.ZoneCount;
        return new DenonReceiverCapabilities(
            SupportsHttp: true,
            SupportsTelnet: null,
            SupportsAppCommand: supportsAppCommand,
            SupportsAppCommand0300: null,
            SupportsZone2: zoneCount is >= 2,
            SupportsZone3: zoneCount is >= 3,
            ZoneCount: zoneCount);
    }

    /// <summary>
    /// Physical speaker transport is based on device-reported identity or a successfully
    /// probed speaker endpoint, NOT the optional user-selected compatibility profile.
    /// An X6700H forced into legacy-goform must keep its HTTPS speaker configuration.
    /// </summary>
    private IDenonReceiverProfile GetSpeakerTransportProfile()
    {
        var selected = GetReceiverProfile();
        if (_verifiedSpeakerProfile is not null)
            return _verifiedSpeakerProfile;
        if (DeviceInfo is null || !HttpPort.HasValue)
            return selected;
        return DenonReceiverProfileRegistry.SelectSpeakerTransportProfile(
            DeviceInfo, HttpPort.Value, selected);
    }

    /// <summary>
    /// An unknown API-0301 receiver may share an existing HTTPS/HTTP speaker protocol.
    /// Check actual read-only level and distance replies before enabling that provider.
    /// Bounded probes never run for a known legacy/port-80 receiver.
    /// </summary>
    private async Task<DenonSpeakerCalibrationSnapshot?> TryDiscoverModernCalibrationAsync(
        CancellationToken cancellationToken)
    {
        if (!CanProbeModernCalibration())
            return null;
        // <-----------


        foreach (var candidate in DenonReceiverProfileRegistry.ModernCalibrationProfiles)
        {
            using (var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                probe.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var result = await ReadModernCalibrationAsync(candidate, probe.Token).ConfigureAwait(false);
                    if (result.Channels.Any(x => x.LevelDecibels.HasValue) &&
                        result.Channels.Any(x => x.DistanceMeters.HasValue))
                    {
                        _verifiedSpeakerProfile = candidate;
                        return result;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                    (ex is HttpRequestException || ex is IOException || ex is InvalidDataException ||
                     ex is OperationCanceledException || ex is System.Xml.XmlException))
                {
                    // Candidate is not verified; never classify it as supported.
                }
            }
        }

        _modernCalibrationProbeFailed = true;
        return null;
        // <-----------
    }

    private IDenonReceiverProfile GetReceiverProfile()
    {
        ThrowIfDisposed();
        return _receiverProfile ?? throw new InvalidOperationException(
            "InitializeAsync muss vor dem Abruf receiver-spezifischer Funktionen aufgerufen werden.");
    }

    private static string NormalizeHost(string host)
    {
        var normalized = host.Trim();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("Es werden nur HTTP-Adressen unterstützt.", nameof(host));
            }

            return uri.Host;
        }

        return normalized.Trim('[', ']');
    }

    /// <summary>Raises the Main Zone volume by one receiver step through the selected transport.</summary>
    public Task VolumeUpAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.MainZoneVolume, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.VolumeUp, token),
            token => _telnetClient.VolumeUpAsync(token),
            cancellationToken,
            allowTelnetFallback: false);

    /// <summary>Lowers the Main Zone volume by one receiver step through the selected transport.</summary>
    public Task VolumeDownAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.MainZoneVolume, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.VolumeDown, token),
            token => _telnetClient.VolumeDownAsync(token),
            cancellationToken,
            allowTelnetFallback: false);

    /// <summary>Raises Zone 2 volume by one receiver step through the selected transport.</summary>
    public Task Zone2VolumeUpAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone2Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.ZoneVolumeUp(2), token),
            token => _telnetClient.ChangeZone2VolumeAsync(true, token),
            cancellationToken,
            allowTelnetFallback: false);

    /// <summary>Lowers Zone 2 volume by one receiver step through the selected transport.</summary>
    public Task Zone2VolumeDownAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone2Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.ZoneVolumeDown(2), token),
            token => _telnetClient.ChangeZone2VolumeAsync(false, token),
            cancellationToken,
            allowTelnetFallback: false);

    /// <summary>Raises Zone 3 volume by one receiver step through the selected transport.</summary>
    public Task Zone3VolumeUpAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone3Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.ZoneVolumeUp(3), token),
            token => _telnetClient.ChangeZone3VolumeAsync(true, token),
            cancellationToken,
            allowTelnetFallback: false);
    /// <summary>Lowers Zone 3 volume by one receiver step through the selected transport.</summary>
    public Task Zone3VolumeDownAsync(DenonControlProtocol protocol, CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone3Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.ZoneVolumeDown(3), token),
            token => _telnetClient.ChangeZone3VolumeAsync(false, token),
            cancellationToken,
            allowTelnetFallback: false);


    /// <summary>Sets Zone 3 muting through the selected transport.</summary>
    public Task SetZone3MuteAsync(
        bool muted,
        DenonControlProtocol protocol,
        CancellationToken cancellationToken = default) =>
        ExecuteControlAsync(AvrFeature.Zone3Control, protocol,
            token => SendHttpCommandAsync(DenonEndpoints.SetZoneMute(3, muted), token),
            token => _telnetClient.SetZone3MuteAsync(muted, token),
            cancellationToken);

    /// <summary>Switches Zone 2 on or to standby through Telnet. No HTTP endpoint is known for this command.</summary>
    public async Task SetZone2PowerAsync(
        bool on,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.Zone2Power,
            protocol,
            token => _telnetClient.SetZone2PowerAsync(on, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Switches Zone 3 on or to standby through Telnet. No HTTP endpoint is known for this command.</summary>
    public async Task SetZone3PowerAsync(
        bool on,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.Zone3Power,
            protocol,
            token => _telnetClient.SetZone3PowerAsync(on, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects a Zone 2 input through Telnet using a display or Denon protocol name.</summary>
    public async Task SetZone2InputAsync(
        string input,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.Zone2Input,
            protocol,
            token => _telnetClient.SetZone2InputAsync(input, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects a Zone 3 input through Telnet using a display or Denon protocol name.</summary>
    public async Task SetZone3InputAsync(
        string input,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.Zone3Input,
            protocol,
            token => _telnetClient.SetZone3InputAsync(input, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resets all speaker channel levels to Denon's receiver factory defaults through Telnet.</summary>
    public async Task ResetSpeakerLevelsToFactoryDefaultsAsync(
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.ChannelLevelControl,
            protocol,
            _telnetClient.ResetSpeakerLevelsToFactoryDefaultsAsync,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the digital input decoder mode through Telnet and returns the raw <c>DC</c> status line.</summary>
    public Task<string> GetDigitalInputModeAsync(
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default) =>
        ExecuteTelnetFeatureAsync(
            AvrFeature.DigitalInputModeControl,
            protocol,
            _telnetClient.QueryDigitalInputModeAsync,
            cancellationToken);

    /// <summary>Sets the digital input decoder to <c>Auto</c>, <c>PCM</c> or <c>DTS</c> through Telnet.</summary>
    public async Task SetDigitalInputModeAsync(
        string mode,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.DigitalInputModeControl,
            protocol,
            token => _telnetClient.SetDigitalInputModeAsync(mode, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Switches Speaker Preset 1 or 2 through Telnet (<c>SPPR</c>). Unlike
    /// <see cref="SelectSpeakerPresetAsync"/>, the active preset is not confirmed by a readback;
    /// a matching <c>SPPR</c> status line is only a plausible response.
    /// </summary>
    public async Task SelectSpeakerPresetTelnetAsync(
        int preset,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.SpeakerPresetControl,
            protocol,
            token => _telnetClient.SelectSpeakerPresetAsync(preset, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets a specific surround mode through Telnet, for example <c>Dolby Surround</c>,
    /// <c>DTS Neural:X</c>, <c>Stereo</c>, <c>Multi Ch Stereo</c>, <c>Pure Direct</c> or <c>Auto</c>.
    /// </summary>
    public async Task SetSurroundModeAsync(
        string mode,
        DenonControlProtocol protocol = DenonControlProtocol.Auto,
        CancellationToken cancellationToken = default)
    {
        _ = await ExecuteTelnetFeatureAsync(
            AvrFeature.SurroundModeControl,
            protocol,
            token => _telnetClient.SetSurroundModeAsync(mode, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteControlAsync(
        AvrFeature feature,
        DenonControlProtocol protocol,
        Func<CancellationToken, Task> httpCommand,
        Func<CancellationToken, Task<string>> telnetCommand,
        CancellationToken cancellationToken,
        bool allowTelnetFallback = true)
    {
        ThrowIfDisposed();
        ISet<DenonControlProtocol> supportedProtocols = GetSupportedProtocols(feature);
        switch (protocol)
        {
            case DenonControlProtocol.Http:
                EnsureProtocolSupportsFeature(DenonControlProtocol.Http, feature, supportedProtocols);
                await httpCommand(cancellationToken).ConfigureAwait(false);
                return;
            // <-----------
            case DenonControlProtocol.Telnet:
                EnsureProtocolSupportsFeature(DenonControlProtocol.Telnet, feature, supportedProtocols);
                _ = await telnetCommand(cancellationToken).ConfigureAwait(false);
                return;
            // <-----------
            case DenonControlProtocol.Auto:
                var useHttpFirst = HttpPort is not null && supportedProtocols.Contains(DenonControlProtocol.Http);
                if (!useHttpFirst)
                {
                    EnsureProtocolSupportsFeature(DenonControlProtocol.Telnet, feature, supportedProtocols);
                    _ = await telnetCommand(cancellationToken).ConfigureAwait(false);
                    return;
                    // <-----------
                }

                // Relative step commands must never be repeated: the receiver may already have
                // applied the step although the HTTP response failed or timed out.
                try
                {
                    await httpCommand(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (allowTelnetFallback &&
                                                  !cancellationToken.IsCancellationRequested &&
                                                  exception is HttpRequestException or TaskCanceledException)
                {
                    EnsureProtocolSupportsFeature(DenonControlProtocol.Telnet, feature, supportedProtocols);
                    _ = await telnetCommand(cancellationToken).ConfigureAwait(false);
                }
                return;
            // <-----------
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, null);
                // <-----------
        }
    }
}
