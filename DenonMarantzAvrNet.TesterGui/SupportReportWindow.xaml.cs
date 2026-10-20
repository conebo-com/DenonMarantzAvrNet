using DenonMarantzAvrNet;
using DenonMarantzAvrNet.Exceptions;
using DenonMarantzAvrNet.Models;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace DenonMarantzAvrNet.TesterGui
{
    public partial class SupportReportWindow : Window
    {
        private const string RemovedText = "[removed]";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
        };

        private readonly CancellationTokenSource _lifetime = new();
        private readonly ObservableCollection<ReportEntry> _entries = new();
        private readonly DenonAvrClient _client;
        private readonly DenonTelnetClient _telnetClient;
        private readonly string? _detectedProfileId;
        private readonly string? _enteredReceiverType;
        private readonly bool _isCompatibilityMode;


        public SupportReportWindow(
                    Window owner,
                    DenonAvrClient client,
                    DenonTelnetClient telnetClient,
                    string? detectedProfileId,
                    string? enteredReceiverType)
        {
            InitializeComponent();
            ApplyLanguage();

            Owner = owner;
            _client = client;
            _telnetClient = telnetClient;
            _detectedProfileId = detectedProfileId;
            _enteredReceiverType = enteredReceiverType;

            _isCompatibilityMode = !string.Equals(
                detectedProfileId,
                client.ReceiverProfileId,
                StringComparison.OrdinalIgnoreCase);

            LstOperations.ItemsSource = _entries;
            BtnPrepare.IsEnabled = false;

            if (_isCompatibilityMode)
            {
                TxtCompatibilityMode.Text = BuildCompatibilityText();
                BrdCompatibilityMode.Visibility = Visibility.Visible;
            }
        }

        private void ApplyLanguage()
        {
            Title = Loc.Get("Check receiver compatibility");
            TxtIntro.Text = Loc.Get("Check whether your receiver works with the functions of this tester and help the project with the result.\n1. Choose the report type below. 'Anonymized' leaves out your MAC address and host.\n2. Click 'Run read-only operations'. Values are only read; nothing is changed on your receiver.\n3. Check the results in the list: green = ok, yellow = warning, red = error.\n4. Click 'Prepare for sending', copy the report and paste it into an e-mail yourself. Nothing is sent automatically.");
            LblReportType.Text = Loc.Get("Report type:");
            RdoRegular.Content = Loc.Get("Regular");
            RdoAnonymized.Content = Loc.Get("Anonymized (no MAC address, serial number or host)");
            BtnRun.Content = Loc.Get("Run read-only operations");
            LblResultHeading.Text = Loc.Get("Result of the selected operation");
            LblIndividualInformation.Text = Loc.Get("Individual information (what ever you want to tell us ;-)");
            ChkMayContact.Content = Loc.Get("The project may contact me by e-mail if there are questions about this report");
            ChkProjectNews.Content = Loc.Get("Keep me informed by e-mail about changes to the project");
            BtnPrepare.Content = Loc.Get("Prepare for sending");
        }

        protected override void OnClosed(EventArgs eventArgs)
        {
            _lifetime.Cancel();
            base.OnClosed(eventArgs);
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs eventArgs)
        {
            BtnRun.IsEnabled = false;
            BtnPrepare.IsEnabled = false;
            _entries.Clear();
            TxtResult.Clear();

            try
            {
                foreach (var operation in BuildOperations())
                {
                    TxtProgress.Text = Loc.Format("Running '{0}' ...", operation.Name);
                    _entries.Add(await RunOperationAsync(operation));
                }

                var okCount = _entries.Count(entry => entry.Status == ReportStatus.Ok);
                var warningCount = _entries.Count(entry => entry.Status == ReportStatus.Warning);
                var errorCount = _entries.Count(entry => entry.Status == ReportStatus.Error);
                TxtProgress.Text = Loc.Format(
                    "Done: {0} operations ({1} ok, {2} warnings, {3} errors).",
                    _entries.Count,
                    okCount,
                    warningCount,
                    errorCount);

                LstOperations.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                return;
                // <-----------
            }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnPrepare.IsEnabled = _entries.Count > 0;
            }
        }

        private void BtnPrepare_Click(object sender, RoutedEventArgs eventArgs)
        {
            var previewWindow = new JsonPreviewWindow(this, BuildReportJson());
            previewWindow.ShowDialog();
        }

        private void LstOperations_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
        {
            TxtResult.Text = LstOperations.SelectedItem is ReportEntry entry ? entry.Output : string.Empty;
        }

        private async Task<ReportEntry> RunOperationAsync(ReportOperation operation)
        {
            try
            {
                var result = await operation.ExecuteAsync(_lifetime.Token);
                return new ReportEntry(operation.Group, operation.Name, result.Status, result.Text);
                // <-----------
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                throw;
                // <-----------
            }
            catch (Exception exception) when (IsExpectedError(exception))
            {
                return new ReportEntry(
                    operation.Group,
                    operation.Name,
                    ReportStatus.Error,
                    $"{exception.GetType().Name}: {exception.Message}");
                // <-----------
            }
        }

        private static bool IsExpectedError(Exception exception) => exception is
            OperationCanceledException or
            DenonAvrException or
            HttpRequestException or
            IOException or
            SocketException or
            XmlException or
            NotSupportedException or
            InvalidOperationException or
            ArgumentException or
            FormatException or
            InvalidDataException;

        // ---------------------------------------------------------------
        // Read-only operations
        // ---------------------------------------------------------------

        private List<ReportOperation> BuildOperations() =>
        [
            new("Receiver", "Probe Telnet", ProbeTelnetAsync),
            new("Main Zone", "Status", ReadStatusAsync),
            new("Main Zone", "Available inputs", ReadInputsAsync),
            new("Receiver", "Receiver info", _ => Task.FromResult(DescribeReceiver())),
            new("Speakers (HTTP)", "Active speaker preset", ReadActivePresetAsync),
            new("Speakers (HTTP)", "Preset levels", ReadPresetLevelsAsync),
            new("Speakers (HTTP)", "Distances", ReadDistancesAsync),
            new("Speakers (HTTP)", "Configured speakers", ReadConfiguredSpeakersAsync),
            new("Receiver", "Legacy HTTP discovery (read only)", ReadLegacyHttpDiscoveryAsync),
            new("Receiver", "Device capabilities (dynamic, read only)", ReadDeviceCapabilitiesAsync),
            new("Receiver", "Advertised read commands (experimental)", ReadAdvertisedReadCommandsAsync),
            new("Speakers (HTTP)", "Channel Level Adjust (per-input, NOT calibration)", ReadChannelAdjustmentsAsync),
            new("Receiver", "Sound mode families (advertised, read only)", ReadSoundFamiliesAsync),
            new("Telnet", "OPSMLALL grouped sound modes (optional read only)", ReadSoundModeOptionsAsync),
            new("Speakers (HTTP)", "Native speaker matrix (read only)", ReadSpeakerMatrixAsync),
            new("Telnet", "CV? raw diagnostic", ReadRawChannelLevelsAsync),
            new("Telnet", "Channel levels", ReadChannelLevelsAsync),
            new("Telnet", "Power (PW?)", token => TelnetQueryAsync(_telnetClient.QueryPowerAsync, token)),
            new("Telnet", "Volume (MV?)", token => TelnetQueryAsync(_telnetClient.QueryVolumeAsync, token)),
            new("Telnet", "Mute (MU?)", token => TelnetQueryAsync(_telnetClient.QueryMuteAsync, token)),
            new("Telnet", "Input (SI?)", token => TelnetQueryAsync(_telnetClient.QueryInputAsync, token)),
            new("Telnet", "Surround mode (MS?)", token => TelnetQueryAsync(_telnetClient.QuerySurroundModeAsync, token)),
            new("Telnet", "Digital input mode (DC?)", token => TelnetQueryAsync(_telnetClient.QueryDigitalInputModeAsync, token)),
            new("Telnet", "Zone 2 (Z2?)", token => ZoneQueryAsync(2, _telnetClient.QueryZone2Async, token)),
            new("Telnet", "Zone 3 (Z3?)", token => ZoneQueryAsync(3, _telnetClient.QueryZone3Async, token))
        ];

        private async Task<OperationResult> ProbeTelnetAsync(CancellationToken cancellationToken)
        {
            var capabilities = await _client.ProbeReceiverCapabilitiesAsync(cancellationToken);

            return capabilities.SupportsTelnet == true
                ? Ok("Telnet (port 23) responded.")
                : Warning("Telnet (port 23) did not respond.");
            // <-----------
        }

        private async Task<OperationResult> ReadStatusAsync(CancellationToken cancellationToken)
        {
            var state = await _client.UpdateAsync(cancellationToken);

            var builder = new StringBuilder();
            builder.AppendLine($"Power:        {state.Power}");
            builder.AppendLine($"Input:        {state.Input ?? "unknown"}");
            builder.AppendLine($"Volume:       {FormatDecibels(state.VolumeDb)}");
            builder.AppendLine($"Mute:         {FormatMute(state.IsMuted)}");
            builder.AppendLine($"Audio input:  {state.Audio?.InputMode ?? "unknown"}");
            builder.AppendLine($"Audio format: {state.Audio?.AudioFormat ?? "unknown"}");
            builder.AppendLine($"Sound mode:   {state.Audio?.SoundMode ?? "unknown"}");
            builder.AppendLine($"Sample rate:  {state.Audio?.SampleRate ?? "unknown"}");
            builder.AppendLine(
                $"Speakers:     {(state.Audio is null || state.Audio.ActiveSpeakers.Count == 0 ? "unknown/none" : string.Join(", ", state.Audio.ActiveSpeakers))}");
            AppendZone(builder, "Zone 2", state.Zone2);
            AppendZone(builder, "Zone 3", state.Zone3);

            if (state.Audio is null)
            {
                builder.AppendLine("Audio information is not available.");
                return Warning(builder.ToString().TrimEnd());
                // <-----------
            }

            return Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private async Task<OperationResult> ReadInputsAsync(CancellationToken cancellationToken)
        {
            var inputs = await _client.RefreshInputsAsync(cancellationToken);

            if (inputs.Count == 0)
            {
                return Warning("The receiver returned no inputs.");
                // <-----------
            }

            return Ok(string.Join(Environment.NewLine, inputs));
            // <-----------
        }

        private OperationResult DescribeReceiver()
        {
            var deviceInfo = _client.DeviceInfo;

            if (deviceInfo is null)
            {
                return Warning("The receiver has not been initialized over HTTP; no device information is available.");
                // <-----------
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Model:             {deviceInfo.ModelName}");
            builder.AppendLine($"Manual model:      {deviceInfo.ManualModelName ?? "-"}");
            builder.AppendLine($"Category:          {deviceInfo.CategoryName ?? "-"}");
            builder.AppendLine($"API version:       {deviceInfo.CommunicationApiVersion ?? "-"}");
            builder.AppendLine($"Zones:             {deviceInfo.ZoneCount?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            builder.AppendLine($"HTTP port:         {_client.HttpPort?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            builder.AppendLine($"AppCommand port:   {_client.AppCommandPort?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
            builder.AppendLine($"AppCmd0300 port:   {_client.AppCommand0300Port?.ToString(CultureInfo.InvariantCulture) ?? "not confirmed"}");
            builder.AppendLine($"Detected profile:  {_detectedProfileId ?? "-"}");
            builder.AppendLine($"Used profile:      {_client.ReceiverProfileId ?? "-"}");
            builder.AppendLine($"Compatibility mode: {(_isCompatibilityMode ? "YES" : "no")}");

            return Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private async Task<OperationResult> ReadActivePresetAsync(CancellationToken cancellationToken)
        {
            if (!_client.IsFeatureAvailable(AvrFeature.SpeakerPresetSelection))
            {
                return Warning("Not available for this receiver / profile.");
                // <-----------
            }

            var preset = await _client.GetActiveSpeakerPresetAsync(cancellationToken);
            return Ok($"Active speaker preset: {preset}");
            // <-----------
        }

        private async Task<OperationResult> ReadPresetLevelsAsync(CancellationToken cancellationToken)
        {
            if (!_client.IsFeatureAvailable(AvrFeature.SpeakerPresetLevelControl))
            {
                return Warning("Not available for this receiver / profile.");
                // <-----------
            }

            var levels = await _client.GetSpeakerPresetLevelsAsync(cancellationToken);

            if (levels.Count == 0)
            {
                return Warning("The receiver returned no preset levels.");
                // <-----------
            }

            var builder = new StringBuilder();
            builder.AppendLine($"{"Index",5}  {"Channel",-42} {"Level",10}");
            foreach (var level in levels)
            {
                builder.AppendLine(
                    $"{level.SpeakerIndex,5}  {level.Channel?.ToString() ?? "Unknown",-42} {FormatDecibels(level.Decibels),10}");
            }

            return Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private async Task<OperationResult> ReadDistancesAsync(CancellationToken cancellationToken)
        {
            if (!_client.IsFeatureAvailable(AvrFeature.SpeakerDistanceControl))
            {
                return Warning("Not available for this receiver / profile.");
                // <-----------
            }

            var configuration = await _client.GetSpeakerDistancesAsync(cancellationToken);

            var builder = new StringBuilder();
            builder.AppendLine($"Receiver unit: {configuration.ReceiverUnit}, step: {FormatMeters(configuration.Step)}");
            builder.AppendLine($"{"Index",5}  {"Channel",-24} {"Distance",10}");
            foreach (var speaker in configuration.Speakers)
            {
                builder.AppendLine(
                    $"{speaker.SpeakerIndex,5}  {speaker.Channel?.ToString() ?? "Unknown",-24} {FormatMeters(speaker.Meters),10}");
            }

            return configuration.Speakers.Count == 0
                ? Warning(builder.ToString().TrimEnd())
                : Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private async Task<OperationResult> ReadConfiguredSpeakersAsync(CancellationToken cancellationToken)
        {
            if (!_client.IsFeatureAvailable(AvrFeature.SpeakerPresetLevelControl) ||
                !_client.IsFeatureAvailable(AvrFeature.SpeakerDistanceControl))
            {
                return Warning("Not available for this receiver / profile.");
                // <-----------
            }

            var speakers = await _client.GetConfiguredSpeakersAsync(cancellationToken);

            if (speakers.Count == 0)
            {
                return Warning("The receiver reported no configured speakers.");
                // <-----------
            }

            var builder = new StringBuilder();
            builder.AppendLine($"{"Index",5}  {"Channel",-24} {"Level",10}  {"Distance",10}");
            foreach (var speaker in speakers)
            {
                builder.AppendLine(
                    $"{speaker.SpeakerIndex,5}  {speaker.Channel,-24} " +
                    $"{FormatDecibels(speaker.LevelDb),10}  {FormatMeters(speaker.DistanceMeters),10}");
            }

            return Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private async Task<OperationResult> ReadChannelAdjustmentsAsync(CancellationToken cancellationToken)
        {
            var adjustments = await _client.GetChannelLevelAdjustmentsAsync(cancellationToken);
            if (adjustments.Count == 0)
                return Warning("GetChLevel returned no channel values. This is NOT calibration.");
            var builder = new StringBuilder();
            builder.AppendLine("Per-input OPTION/Channel Level Adjust; NOT Audyssey calibration levels.");
            builder.AppendLine("Name      Status   SpeakerType  LevelRaw  ValueRaw");
            foreach (var ch in adjustments)
                builder.AppendLine($"{SafeAdjustmentToken(ch.Channel),-9} " +
                    $"{SafeAdjustmentToken(ch.Status),-8} " +
                    $"{SafeAdjustmentToken(ch.SpeakerType),-12} " +
                    $"{SafeAdjustmentToken(ch.LevelRaw),-9} " +
                    $"{SafeAdjustmentToken(ch.ValueRaw)}");
            return Ok(builder.ToString().TrimEnd());
        }

        private static string SafeAdjustmentToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            var trimmed = value.Trim();
            return trimmed.Length <= 24 && trimmed.All(c =>
                char.IsLetterOrDigit(c) || c == '+' || c == '-' ||
                c == '.' || c == '_' || c == ':' || c == '%')
                ? trimmed : "[hidden]";
        }

        private async Task<OperationResult> ReadSoundModeOptionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var modes = await _client.GetSoundModeOptionsAsync(cancellationToken);
                if (modes.Count == 0)
                    return Warning("OPSMLALL completed but supplied no recognized sound modes.");
                var builder = new StringBuilder();
                foreach (var mode in modes)
                    builder.AppendLine($"{mode.FamilyCode} {mode.NativeId} " +
                        $"{(mode.IsActive ? "active" : "-")} {SafeCatalogText(mode.DisplayName)}");
                return Ok(builder.ToString().TrimEnd());
            }
            catch (Exception exception) when (exception is IOException ||
                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Warning("OPSMLALL not verified on this receiver: " + exception.GetType().Name);
            }
        }

        private async Task<OperationResult> ReadSoundFamiliesAsync(CancellationToken cancellationToken)
        {
            var catalog = await _client.RefreshDeviceCapabilitiesAsync(cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine("Main-Zone sound mode family shortcuts from Deviceinfo.xml:");
            foreach (var family in catalog.SoundModeFamilies)
                builder.AppendLine("  " + SafeCatalogText(family));
            builder.AppendLine("Advertised shortcut != verified mode selection; no commands sent.");
            return catalog.SoundModeFamilies.Count == 0
                ? Warning(builder.ToString().TrimEnd()) : Ok(builder.ToString().TrimEnd());
        }

        private async Task<OperationResult> ReadLegacyHttpDiscoveryAsync(CancellationToken cancellationToken)
        {
            var report = await _client.DiscoverLegacyHttpAsync(cancellationToken);
            var builder = new StringBuilder();
            foreach (var probe in report.Probes)
            {
                builder.AppendLine($"{probe.Method} {probe.Path}: {probe.Result} ({probe.Name})");
                if (!string.IsNullOrWhiteSpace(probe.Root))
                    builder.AppendLine($"  Root: {probe.Root}");
                foreach (var path in probe.ElementPaths)
                    builder.AppendLine($"  XML: {path}");
                foreach (var link in probe.DiscoveredLinks)
                    builder.AppendLine($"  Web reference (not fetched): {link}");
                if (!string.IsNullOrEmpty(probe.Note))
                    builder.AppendLine($"  Note: {probe.Note}");
            }
            // No private XML values are copied into the compatibility report.
            return report.Probes.Any(p => p.Result == "Received")
                ? Ok(builder.ToString().TrimEnd())
                : Warning(builder.ToString().TrimEnd());
        }

        private async Task<OperationResult> ReadDeviceCapabilitiesAsync(CancellationToken cancellationToken)
        {
            var catalog = await _client.RefreshDeviceCapabilitiesAsync(cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine($"Deviceinfo port: {catalog.HttpPort}");
            builder.AppendLine($"Advertised command declarations: {catalog.Commands.Count}");
            builder.AppendLine($"Enabled: {catalog.Commands.Count(c => c.Advertised)}");
            builder.AppendLine($"Possible channel types: {catalog.Channels.Count} (not configured speakers)");
            builder.AppendLine($"Sound mode catalog: {catalog.SoundModes.Count} (not signal-dependent)");
            builder.AppendLine($"Max volume: {catalog.MaximumVolume?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            builder.AppendLine($"Volume step: {catalog.VolumeStep?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            builder.AppendLine($"Quick Select slots: {catalog.QuickSelectCount?.ToString() ?? "unknown"}");
            builder.AppendLine($"Tuner presets: {catalog.TunerPresetCount?.ToString() ?? "unknown"}");
            builder.AppendLine("READ COMMAND CLAIMS:");
            foreach (var name in new[] { "GetChLevel", "GetSoundModeList", "GetSoundMode" })
                builder.AppendLine($"  {name}: {(catalog.IsCommandAdvertised(name) ? "advertised" : "not advertised")}");

            builder.AppendLine("CHANNEL TYPES (source: Deviceinfo.xml):");
            foreach (var ch in catalog.Channels)
            {
                // Do not print arbitrary receiver-configured user text into support reports.
                if (SafeCatalogText(ch.NativeCode) == "(unrecognized entry)") continue;
                builder.AppendLine($"  {ch.NativeCode,-5} min={FormatCapabilityNumber(ch.Minimum),-6} " +
                    $"max={FormatCapabilityNumber(ch.Maximum),-6} " +
                    $"default={FormatCapabilityNumber(ch.DefaultValue),-6} " +
                    $"step={FormatCapabilityNumber(ch.Step)}" +
                    (ch.MayBePlaceholder ? " [fixed-range / possible placeholder]" : ""));
            }
            builder.AppendLine("MAIN-ZONE SOUND MODE CATALOG (not current availability):");
            foreach (var mode in catalog.SoundModes)
            {
                // Catalog entries may be firmware strings. Constrain to safe characters.
                var group = SafeCatalogText(mode.Group);
                var name = SafeCatalogText(mode.Name);
                builder.AppendLine($"  {group}: {name}");
            }
            builder.AppendLine("Declared commands are not verified read/write support.");
            return Ok(builder.ToString().TrimEnd());
        }

        private async Task<OperationResult> ReadAdvertisedReadCommandsAsync(CancellationToken cancellationToken)
        {
            var results = await _client.ProbeAdvertisedReadCommandsAsync(cancellationToken);
            var builder = new StringBuilder();
            foreach (var result in results)
            {
                builder.AppendLine($"{result.Command} @ {result.Endpoint}:{result.HttpPort} = {result.Status}");
                foreach (var field in result.ResponseFields.Take(40))
                    builder.AppendLine($"  XML: {field}");
                if (!string.IsNullOrEmpty(result.Note)) builder.AppendLine($"  Note: {result.Note}");
            }
            builder.AppendLine("Only read queries were issued. XML VALUES were deliberately excluded.");
            return results.Any(r => r.Status == "Received")
                ? Ok(builder.ToString().TrimEnd()) : Warning(builder.ToString().TrimEnd());
        }

        private static string FormatCapabilityNumber(double? value) =>
            value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?";

        private static string SafeCatalogText(string value) =>
            value.Length <= 64 && value.All(c => char.IsLetterOrDigit(c) ||
                c == ' ' || c == '-' || c == '/' || c == '+' || c == '.' || c == '&')
                ? value : "(unrecognized entry)";

        private async Task<OperationResult> ReadSpeakerMatrixAsync(CancellationToken cancellationToken)
        {
            var matrix = await _client.DiscoverSpeakerMatrixAsync(cancellationToken);
            var builder = new StringBuilder();
            builder.AppendLine($"Response: {matrix.Status}; port: {matrix.HttpPort?.ToString() ?? "unknown"}");
            builder.AppendLine($"Native speaker slots: {matrix.Slots.Count}");
            builder.AppendLine($"{"Native ID",-20} {"Control",-10} Label");
            foreach (var slot in matrix.Slots)
            {
                // Support reports may be forwarded to third parties. Only display
                // standard short channel tokens, not arbitrary/user-entered names.
                var label = IsSafeSpeakerCode(slot.Label) ? slot.Label :
                    string.IsNullOrEmpty(slot.Label) ? "(none)" : "(custom label withheld)";
                var control = slot.ControlCode ?? "(none)";
                if (slot.ControlCode != null && (slot.ControlCode.Length > 3 ||
                    !slot.ControlCode.All(ch => ch >= '0' && ch <= '9')))
                    control = "(non-numeric)";
                builder.AppendLine($"{slot.NativeId,-20} {control,-10} {label}");
            }
            builder.AppendLine("Control values are raw AVR codes; no universal meaning is assumed.");
            builder.AppendLine("These are neither Audyssey calibration levels nor speaker distances.");
            return matrix.Status == "Received"
                ? Ok(builder.ToString().TrimEnd())
                : Warning(builder.ToString().TrimEnd());
        }

        private static bool IsSafeSpeakerCode(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 12) return false;
            // Exact token set: never leak arbitrary custom room/speaker names.
            var labels = new[] { "FL", "FR", "C", "SW", "SW1", "SW2", "SW3", "SW4",
                "SL", "SR", "SBL", "SBR", "SB", "FHL", "FHR", "RHL", "RHR",
                "TFL", "TFR", "TML", "TMR", "TRL", "TRR", "FWL", "FWR",
                "FDL", "FDR", "SDL", "SDR", "TS", "SHL", "SHR" };
            return labels.Any(code => string.Equals(code, value,
                StringComparison.OrdinalIgnoreCase));
        }

        private async Task<OperationResult> ReadRawChannelLevelsAsync(CancellationToken cancellationToken)
        {
            var lines = await _telnetClient.QueryRawChannelLevelsAsync(cancellationToken);
            var builder = new StringBuilder();
            foreach (var line in lines) builder.AppendLine(line);
            var completed = lines.Any(line => string.Equals(line, "CVEND", StringComparison.OrdinalIgnoreCase));
            var channels = lines.Count(line => line.StartsWith("CV", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(line, "CVEND", StringComparison.OrdinalIgnoreCase));
            builder.AppendLine($"Response complete: {completed}; channel entries: {channels}");
            builder.AppendLine("CV reports channel-level adjustment, not calibration or wiring.");
            return completed ? Ok(builder.ToString().TrimEnd()) : Warning(builder.ToString().TrimEnd());
        }

        private async Task<OperationResult> ReadChannelLevelsAsync(CancellationToken cancellationToken)
        {
            var levels = await _client.GetSpeakerLevelsAsync(DenonControlProtocol.Telnet, cancellationToken);

            if (levels.Count == 0)
            {
                return Warning("The receiver returned no configured channel levels.");
                // <-----------
            }

            var builder = new StringBuilder();
            foreach (var level in levels)
            {
                var levelText = level.IsOff ? "OFF" : FormatDecibels(level.Decibels);
                builder.AppendLine($"{level.Channel,-24} {levelText,10}   ({level.RawResponse})");
            }

            return Ok(builder.ToString().TrimEnd());
            // <-----------
        }

        private static async Task<OperationResult> TelnetQueryAsync(
            Func<CancellationToken, Task<string>> query,
            CancellationToken cancellationToken)
        {
            var response = await query(cancellationToken);
            return Ok($"Response: {response}");
            // <-----------
        }

        private async Task<OperationResult> ZoneQueryAsync(
            int zone,
            Func<CancellationToken, Task<string>> query,
            CancellationToken cancellationToken)
        {
            var capabilities = _client.ReceiverCapabilities;
            var zoneSupported = zone == 2 ? capabilities?.SupportsZone2 : capabilities?.SupportsZone3;

            if (zoneSupported == false)
            {
                return Warning($"Skipped: the receiver reports no Zone {zone}.");
                // <-----------
            }

            return await TelnetQueryAsync(query, cancellationToken);
            // <-----------
        }

        // ---------------------------------------------------------------
        // Report
        // ---------------------------------------------------------------

        private string BuildCompatibilityText() =>
            $"Compatibility mode: the AVR reports '{_client.DeviceInfo?.ModelName ?? "unknown"}' " +
            $"(detected profile '{_detectedProfileId ?? "none"}'), " +
            $"but profile '{_client.ReceiverProfileId ?? "none"}' was selected manually and is used.";

        private string BuildReportJson()
        {
            var anonymized = RdoAnonymized.IsChecked == true;
            var deviceInfo = _client.DeviceInfo;

            var report = new
            {
                ReportVersion = 1,
                CreatedUtc = DateTimeOffset.UtcNow,
                Anonymized = anonymized,
                CompatibilityMode = new
                {
                    Active = _isCompatibilityMode,
                    DetectedProfile = _detectedProfileId,
                    UsedProfile = _client.ReceiverProfileId,
                    ReportedModelName = deviceInfo?.ModelName,
                    Notice = _isCompatibilityMode
                        ? BuildCompatibilityText()
                        : "Not used. The profile detected for the AVR was used."
                },
                Receiver = new
                {
                    ModelName = deviceInfo?.ModelName,
                    ModelNameReported = !string.IsNullOrWhiteSpace(deviceInfo?.ModelName),
                    UserEnteredReceiverType = _enteredReceiverType,
                    ManualModelName = deviceInfo?.ManualModelName,
                    CategoryName = deviceInfo?.CategoryName,
                    CommunicationApiVersion = deviceInfo?.CommunicationApiVersion,
                    ZoneCount = deviceInfo?.ZoneCount,
                    HttpPort = _client.HttpPort,
                    AppCommandPort = _client.AppCommandPort,
                    AppCommand0300Port = _client.AppCommand0300Port,
                    MacAddress = anonymized ? null : deviceInfo?.MacAddress
                },
                Capabilities = _client.ReceiverCapabilities,
                Operations = _entries
                    .Select(entry => new
                    {
                        entry.Group,
                        entry.Name,
                        entry.Status,
                        OutputLines = ToOutputLines(entry.Output, anonymized)
                    })
                    .ToArray(),
                User = new
                {
                    Message = string.Empty,
                    IndividualInformation = TxtIndividualInformation.Text.Trim(),
                    MayContactByMail = ChkMayContact.IsChecked == true,
                    WantsProjectNewsByMail = ChkProjectNews.IsChecked == true
                }
            };

            return JsonSerializer.Serialize(report, JsonOptions);
            // <-----------
        }

        private string[] ToOutputLines(string output, bool anonymized)
        {
            var text = output;

            if (anonymized)
            {
                text = NetFrameworkCompat.Replace(text, _client.Host, RemovedText, StringComparison.OrdinalIgnoreCase);

                var macAddress = _client.DeviceInfo?.MacAddress;
                if (!string.IsNullOrWhiteSpace(macAddress))
                {
                    text = NetFrameworkCompat.Replace(text, macAddress!, RemovedText, StringComparison.OrdinalIgnoreCase);
                }
            }

            text = System.Text.RegularExpressions.Regex.Replace(text, "\r\n|[\r\n\f\u0085\u2028\u2029]", "\n");

            return text.Split('\n');
            // <-----------
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static OperationResult Ok(string text) => new(ReportStatus.Ok, text);

        private static OperationResult Warning(string text) => new(ReportStatus.Warning, text);

        private static void AppendZone(StringBuilder builder, string name, DenonZoneState? zone)
        {
            if (zone is not null)
            {
                builder.AppendLine(
                    $"{name}:       {zone.Power}, {zone.Input ?? "unknown"}, " +
                    $"{FormatDecibels(zone.VolumeDb)}, Mute {FormatMute(zone.IsMuted)}");
            }
        }

        private static string FormatDecibels(double? value) =>
            value is null ? "-" : $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)} dB";

        private static string FormatMeters(double? value) =>
            value is null ? "-" : $"{value.Value.ToString("0.00", CultureInfo.InvariantCulture)} m";

        private static string FormatMute(bool? muted) => muted switch
        {
            true => "On",
            false => "Off",
            null => "unknown"
        };
    }

    internal enum ReportStatus
    {
        Ok,
        Warning,
        Error
    }

    internal sealed record OperationResult(ReportStatus Status, string Text);

    internal sealed record ReportOperation(
        string Group,
        string Name,
        Func<CancellationToken, Task<OperationResult>> ExecuteAsync);

    internal sealed record ReportEntry(string Group, string Name, ReportStatus Status, string Output)
    {
        public string DisplayName => $"{Group}: {Name}";

        public Brush StatusBrush => Status switch
        {
            ReportStatus.Ok => Brushes.Green,
            ReportStatus.Warning => Brushes.Gold,
            _ => Brushes.Red
        };
    }
}