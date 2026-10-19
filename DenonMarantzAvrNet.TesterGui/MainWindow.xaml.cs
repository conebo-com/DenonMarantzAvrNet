using DenonMarantzAvrNet.Exceptions;
using DenonMarantzAvrNet.Logger;
using DenonMarantzAvrNet.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml;
using System.Reflection;

namespace DenonMarantzAvrNet.TesterGui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public const string EMail = "contact@conebo.com";

        private static string CommandSentText => Loc.Get("Command sent. This command returns no response text.");
        private static string VersionText => Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        private static readonly Regex LogHeaderRegex = new(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[(?<source>[A-Z]+)\] ",
            RegexOptions.Compiled);

        private readonly CancellationTokenSource _lifetime = new();
        private readonly string _logFilePath = Path.Combine(Path.GetTempPath(), "DenonMarantzTester.log");
        private DenonAvrClient? _client;
        private DenonTelnetClient? _telnetClient;
        private string? _detectedProfileId;
        private long _logPosition;
        private Func<string> _connectionStatus = () => Loc.Get("Not connected");
        private Func<string>? _compatibilityStatus;
        private string? _enteredReceiverType;

        public MainWindow()
        {
            InitializeComponent();
            GrdContent.IsEnabled = false;
            ReceiverLogger.FilePath = _logFilePath;
            ReceiverLogger.Clear();
            ReceiverLogger.Enabled = true;
            ReceiverLogger.TelnetLoggingEnabled = true;

            CmbProtocol.ItemsSource = Enum.GetValues(typeof(DenonControlProtocol));

            CmbProtocol.SelectedItem = DenonControlProtocol.Auto;

            SelectLanguage(Loc.IsGerman);

            ApplyLanguage();
        }

        private CancellationToken Token => _lifetime.Token;

        private DenonAvrClient Client =>
            _client ?? throw new InvalidOperationException(Loc.Get("Connect to a receiver first."));
        // <-----------

        private DenonTelnetClient Telnet =>
            _telnetClient ?? throw new InvalidOperationException(Loc.Get("Connect to a receiver first."));
        // <-----------

        protected override void OnClosed(EventArgs eventArgs)
        {
            _lifetime.Cancel();
            DisposeClient();
            base.OnClosed(eventArgs);
        }

        private void TglEnglish_Click(object sender, RoutedEventArgs eventArgs)
        {
            SelectLanguage(false);
        }

        private void TglGerman_Click(object sender, RoutedEventArgs eventArgs)
        {
            SelectLanguage(true);
        }

        private void SelectLanguage(bool german)
        {
            const double dimmedOpacity = 0.4;

            TglEnglish.IsChecked = !german;
            TglGerman.IsChecked = german;
            ImgFlagEnglish.Opacity = german ? dimmedOpacity : 1.0;
            ImgFlagGerman.Opacity = german ? 1.0 : dimmedOpacity;

            if (Loc.IsGerman == german)
            {
                return;
                // <-----------
            }

            Loc.IsGerman = german;
            ApplyLanguage();
        }

        private void SetConnectionStatus(Func<string> textProvider)
        {
            _connectionStatus = textProvider;
            TxtConnectionStatus.Text = textProvider();
        }

        private void ApplyLanguage()
        {
            Title = $"{Loc.Get("Denon / Marantz AVR Tester")} v{VersionText}";
            LblHost.Text = Loc.Get("Host:");
            BtnConnect.Content = Loc.Get("Connect");
            BtnOpenWebPage.Content = Loc.Get("Open receivers web page");
            LblLanguage.Text = Loc.Get("Language:");
            LblProtocol.Text = Loc.Get("Protocol");
            LblResult.Text = Loc.Get("Result");
            LblHttpOutput.Text = Loc.Get("HTTP output");
            LblTelnetOutput.Text = Loc.Get("Telnet output");
            ChkClearTrafficBeforeOperation.Content = Loc.Get("Clear HTTP / Telnet output before every operation");
            BtnClear.Content = Loc.Get("Clear outputs");
            BtnSend.Content = Loc.Get("Send");
            TxtConnectionStatus.Text = _connectionStatus();
            TxtSupportReportHint.Text = Loc.Get("Help the project: check whether your receiver is compatible with the functions offered here. Only read-only operations are run against your receiver, and you can send the result to the project by e-mail. Nothing is sent automatically and nothing is changed on the receiver.");
            BtnSupportReport.Content = Loc.Get("Check receiver compatibility...");

            if (_compatibilityStatus is not null)
            {
                TxtCompatibilityMode.Text = _compatibilityStatus();
            }

            RebuildCommands();
        }

        private void RebuildCommands()
        {
            var previousIndex = -1;
            if (LstCommands.ItemsSource is ListCollectionView { SourceCollection: List<TesterCommand> previousCommands } &&
                LstCommands.SelectedItem is TesterCommand selectedCommand)
            {
                previousIndex = previousCommands.IndexOf(selectedCommand);
            }

            var parameter1 = TxtParameter1.Text;
            var parameter2 = TxtParameter2.Text;
            var commands = BuildCommands();
            var commandView = new ListCollectionView(commands);
            commandView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TesterCommand.Group)));
            LstCommands.ItemsSource = commandView;

            if (previousIndex >= 0)
            {
                LstCommands.SelectedItem = commands[previousIndex];
                TxtParameter1.Text = parameter1;
                TxtParameter2.Text = parameter2;
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs eventArgs)
        {
            var host = TxtHost.Text.Trim();
            if (host.Length == 0)
            {
                TxtResult.Text = Loc.Get("Enter a host or IP address.");
                return;
                // <-----------
            }

            ClearTrafficOutputIfRequested();
            BtnConnect.IsEnabled = false;
            SetConnectionStatus(() => Loc.Get("Connecting ..."));
            TxtCompatibilityMode.Visibility = Visibility.Collapsed;
            _compatibilityStatus = null;

            try
            {
                DisposeClient();
                var client = new DenonAvrClient(host);
                _client = client;
                _telnetClient = new DenonTelnetClient(client.Host);
                var device = await client.InitializeAsync(Token);

                var modelName = device.ModelName;
                _detectedProfileId = client.ReceiverProfileId;

                var profileDialog = new ReceiverProfileDialog(this, client.ReceiverProfileId, modelName);
                if (profileDialog.ShowDialog() == true &&
                    profileDialog.SelectedProfileId is { } selectedProfileId)
                {
                    _enteredReceiverType = profileDialog.EnteredReceiverType;
                    client.SelectReceiverProfile(selectedProfileId);
                    if (!string.Equals(_detectedProfileId, client.ReceiverProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        var usedProfileId = client.ReceiverProfileId;
                        _compatibilityStatus = () => Loc.Format(
                            "Compatibility mode: AVR reports '{0}', using profile '{1}'",
                            modelName,
                            usedProfileId);
                        TxtCompatibilityMode.Text = _compatibilityStatus();
                        TxtCompatibilityMode.Visibility = Visibility.Visible;
                    }
                }

                SetConnectionStatus(() => Loc.Format("Connected: {0}", modelName));
                TxtResult.Text = DescribeReceiver();
            }
            catch (Exception exception) when (IsExpectedError(exception))
            {
                SetConnectionStatus(() => Loc.Get("HTTP initialization failed (Telnet commands may still work)"));
                TxtResult.Text = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                RefreshTrafficOutput();
                GrdContent.IsEnabled = _client is not null;
                BtnConnect.IsEnabled = true;
            }
        }

        private async void BtnSend_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (LstCommands.SelectedItem is not TesterCommand command)
            {
                TxtResult.Text = Loc.Get("Select a command first.");
                return;
                // <-----------
            }

            ClearTrafficOutputIfRequested();

            var input = new CommandInput(
                TxtParameter1.Text,
                TxtParameter2.Text,
                CmbProtocol.SelectedItem is DenonControlProtocol protocol ? protocol : DenonControlProtocol.Auto);

            BtnSend.IsEnabled = false;
            TxtResult.Text = Loc.Format("Running '{0}' ...", command.Name);

            try
            {
                TxtResult.Text = await command.ExecuteAsync(input);
            }
            catch (Exception exception) when (IsExpectedError(exception))
            {
                TxtResult.Text = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                RefreshTrafficOutput();
                BtnSend.IsEnabled = true;
            }
        }

        private void ClearTrafficOutputIfRequested()
        {
            if (ChkClearTrafficBeforeOperation.IsChecked == true)
            {
                TxtHttpOutput.Clear();
                TxtTelnetOutput.Clear();
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs eventArgs)
        {
            TxtResult.Clear();
            TxtHttpOutput.Clear();
            TxtTelnetOutput.Clear();
        }

        private void LstCommands_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
        {
            if (LstCommands.SelectedItem is not TesterCommand command)
            {
                StcParameters.Visibility = Visibility.Collapsed;
                return;
                // <-----------
            }

            StcParameters.Visibility = Visibility.Visible;
            ApplyParameter(LblParameter1, TxtParameter1, command.Parameters, 0);
            ApplyParameter(LblParameter2, TxtParameter2, command.Parameters, 1);
            StcProtocol.Visibility = command.UsesProtocol ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void ApplyParameter(
            TextBlock label,
            TextBox textBox,
            IReadOnlyList<CommandParameter> parameters,
            int index)
        {
            if (index >= parameters.Count)
            {
                label.Visibility = Visibility.Collapsed;
                textBox.Visibility = Visibility.Collapsed;
                return;
                // <-----------
            }

            label.Text = parameters[index].Label;
            textBox.Text = parameters[index].DefaultValue;
            label.Visibility = Visibility.Visible;
            textBox.Visibility = Visibility.Visible;
        }

        private void DisposeClient()
        {
            _client?.Dispose();
            _client = null;
            _telnetClient = null;
            _detectedProfileId = null;
            _enteredReceiverType = null;
            GrdContent.IsEnabled = false;
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
        // Traffic output (HTTP / Telnet) read from the receiver log file
        // ---------------------------------------------------------------

        private void RefreshTrafficOutput()
        {
            try
            {
                AppendTraffic(ReadNewLogText());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TxtHttpOutput.AppendText(Loc.Format("[Log read error] {0}", exception.Message) + Environment.NewLine);
            }
        }

        private string ReadNewLogText()
        {
            if (!File.Exists(_logFilePath))
            {
                return string.Empty;
                // <-----------
            }

            using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _logPosition)
            {
                _logPosition = 0;
            }

            stream.Seek(_logPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var text = reader.ReadToEnd();
            _logPosition = stream.Position;
            return text;
            // <-----------
        }

        private void AppendTraffic(string logText)
        {
            var httpBuilder = new StringBuilder();
            var telnetBuilder = new StringBuilder();
            StringBuilder? current = null;

            using var reader = new StringReader(logText);
            while (reader.ReadLine() is { } line)
            {
                var match = LogHeaderRegex.Match(line);
                if (match.Success)
                {
                    current = match.Groups["source"].Value switch
                    {
                        "HTTP" => httpBuilder,
                        "TELNET" => telnetBuilder,
                        _ => null
                    };
                }

                current?.AppendLine(line);
            }

            if (httpBuilder.Length > 0)
            {
                TxtHttpOutput.AppendText(httpBuilder.ToString());
                TxtHttpOutput.ScrollToEnd();
            }

            if (telnetBuilder.Length > 0)
            {
                TxtTelnetOutput.AppendText(telnetBuilder.ToString());
                TxtTelnetOutput.ScrollToEnd();
            }
        }

        // ---------------------------------------------------------------
        // Command menu
        // ---------------------------------------------------------------

        private List<TesterCommand> BuildCommands()
        {
            var zoneParameter = new CommandParameter(Loc.Get("Zone (2 or 3)"), "2");
            var onOffParameter = new CommandParameter(Loc.Get("State (on/off)"), "on");
            var speakerIndexParameter = new CommandParameter(Loc.Get("Speaker index"), "0");
            var statusGroup = Loc.Get("Status");
            var mainZoneGroup = Loc.Get("Main Zone");
            var zonesGroup = Loc.Get("Zones 2/3");
            var speakersGroup = Loc.Get("Speakers (HTTP)");
            var audioGroup = Loc.Get("Audio / Levels (Telnet)");

            return
            [
                new(statusGroup, Loc.Get("Refresh status"), false, [], _ => ShowStatusAsync()),
                new(statusGroup, Loc.Get("Receiver info"), false, [], _ => Task.FromResult(DescribeReceiver())),
                new(statusGroup, Loc.Get("Probe Telnet (updates capabilities)"), false, [], _ => ProbeTelnetAsync()),

                new(mainZoneGroup, Loc.Get("Power on"), true, [],
                    input => SentAsync(() => Client.PowerOnAsync(input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Standby"), true, [],
                    input => SentAsync(() => Client.PowerOffAsync(input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Volume up"), true, [],
                    input => SentAsync(() => Client.VolumeUpAsync(input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Volume down"), true, [],
                    input => SentAsync(() => Client.VolumeDownAsync(input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Set volume (dB)"), true, [new(Loc.Get("Volume in dB (-80.0 to +18.0)"), "-40.0")],
                    input => SentAsync(() => Client.SetVolumeDbAsync(ParseDouble(input.Parameter1), input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Mute on"), true, [],
                    input => SentAsync(() => Client.SetMuteAsync(true, input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("Mute off"), true, [],
                    input => SentAsync(() => Client.SetMuteAsync(false, input.Protocol, Token))),
                new(mainZoneGroup, Loc.Get("List available inputs"), false, [], _ => ListInputsAsync()),
                new(mainZoneGroup, Loc.Get("Select input"), true, [new(Loc.Get("Input name (e.g. Media Player, TV AUDIO, MPLAY)"), "Media Player")],
                    input => SentAsync(() => Client.SetInputAsync(input.Parameter1, input.Protocol, Token))),

                new(zonesGroup, Loc.Get("Power (Telnet)"), false, [zoneParameter, onOffParameter], ZonePowerAsync),
                new(zonesGroup, Loc.Get("Volume up/down"), true, [zoneParameter, new(Loc.Get("Direction (up/down)"), "up")], ZoneVolumeStepAsync),
                new(zonesGroup, Loc.Get("Set volume (dB)"), true, [zoneParameter, new(Loc.Get("Volume in dB (-80.0 to +18.0)"), "-40.0")], ZoneSetVolumeAsync),
                new(zonesGroup, Loc.Get("Mute"), true, [zoneParameter, onOffParameter], ZoneMuteAsync),
                new(zonesGroup, Loc.Get("Select input (Telnet)"), false, [zoneParameter, new(Loc.Get("Input name"), "Media Player")], ZoneInputAsync),

                new(speakersGroup, Loc.Get("Configured speakers (levels + distances)"), false, [], _ => ShowConfiguredSpeakersAsync()),
                new(speakersGroup, Loc.Get("Preset levels"), false, [], _ => ShowPresetLevelsAsync()),
                new(speakersGroup, Loc.Get("Distances"), false, [], _ => ShowDistancesAsync()),
                new(speakersGroup, Loc.Get("Set preset level"), false, [speakerIndexParameter, new(Loc.Get("Level in dB (-12.0 to +12.0)"), "0.0")],
                    input => SentAsync(() => Client.SetSpeakerPresetLevelAsync(ParseInt(input.Parameter1), ParseDouble(input.Parameter2), Token))),
                new(speakersGroup, Loc.Get("Set distance"), false, [speakerIndexParameter, new(Loc.Get("Distance in meters"), "3.00")],
                    input => SentAsync(() => Client.SetSpeakerDistanceAsync(ParseInt(input.Parameter1), ParseDouble(input.Parameter2), Token))),
                new(speakersGroup, Loc.Get("Read active preset"), false, [], _ => ReadActivePresetAsync()),
                new(speakersGroup, Loc.Get("Switch preset (can take ~15 s)"), false, [new(Loc.Get("Preset number"), "1")], SelectPresetAsync),

                new(audioGroup, Loc.Get("Channel levels"), false, [], _ => ShowChannelLevelsAsync()),
                new(audioGroup, Loc.Get("Set channel level"), false,
                    [new(Loc.Get("Channel (e.g. FrontLeft, Subwoofer)"), "FrontLeft"), new(Loc.Get("Level in dB (-12.0 to +12.0)"), "0.0")],
                    SetChannelLevelAsync),
                new(audioGroup, Loc.Get("Surround mode"), false,
                    [new(Loc.Get("Mode (Auto, Stereo, Dolby Surround, DTS Neural:X, Multi Ch Stereo, Pure Direct)"), "Auto")],
                    input => TelnetResponseAsync(Telnet.SetSurroundModeAsync(input.Parameter1, Token))),
                new(audioGroup, Loc.Get("Digital input decoder"), false, [new(Loc.Get("Mode (Auto, PCM, DTS)"), "Auto")],
                    input => TelnetResponseAsync(Telnet.SetDigitalInputModeAsync(input.Parameter1, Token))),
                new(audioGroup, Loc.Get("Raw Telnet command"), false, [new(Loc.Get("Command (e.g. PW?, MV?, SI?)"), "PW?")],
                    input => TelnetResponseAsync(Telnet.SendCommandAsync(input.Parameter1.Trim(), Token)))
            ];
        }

        private static async Task<string> SentAsync(Func<Task> command)
        {
            await command();
            return CommandSentText;
            // <-----------
        }

        private static async Task<string> TelnetResponseAsync(Task<string> command)
        {
            var response = await command;
            return Loc.Format("Telnet response: {0}", response);
            // <-----------
        }

        private async Task<string> ShowStatusAsync()
        {
            var state = await Client.UpdateAsync(Token);
            var unknownText = Loc.Get("unknown");
            var builder = new StringBuilder();
            builder.AppendLine(Loc.Format("Power:        {0}", state.Power));
            builder.AppendLine(Loc.Format("Input:        {0}", state.Input ?? unknownText));
            builder.AppendLine(Loc.Format("Volume:       {0}", FormatDecibels(state.VolumeDb)));
            builder.AppendLine(Loc.Format("Mute:         {0}", FormatMute(state.IsMuted)));
            builder.AppendLine(Loc.Format("Audio input:  {0}", state.Audio?.InputMode ?? unknownText));
            builder.AppendLine(Loc.Format("Audio format: {0}", state.Audio?.AudioFormat ?? unknownText));
            builder.AppendLine(Loc.Format("Sound mode:   {0}", state.Audio?.SoundMode ?? unknownText));
            builder.AppendLine(Loc.Format("Sample rate:  {0}", state.Audio?.SampleRate ?? unknownText));
            builder.AppendLine(Loc.Format("Speakers:     {0}", FormatActiveSpeakers(state.Audio)));
            AppendZone(builder, "Zone 2", state.Zone2);
            AppendZone(builder, "Zone 3", state.Zone3);
            return builder.ToString();
            // <-----------
        }

        private string DescribeReceiver()
        {
            var builder = new StringBuilder();
            builder.AppendLine(Loc.Format("Host:         {0}", Client.Host));
            builder.AppendLine(Loc.Format("HTTP port:    {0}", Client.HttpPort?.ToString(CultureInfo.InvariantCulture) ?? "-"));
            builder.AppendLine(Loc.Format("Profile:      {0}", Client.ReceiverProfileId ?? "-"));
            builder.AppendLine(Loc.Format("Device info:  {0}", Client.DeviceInfo?.ToString() ?? "-"));
            builder.AppendLine(Loc.Format("Capabilities: {0}", Client.ReceiverCapabilities?.ToString() ?? "-"));
            return builder.ToString();
            // <-----------
        }

        private async Task<string> ProbeTelnetAsync()
        {
            var capabilities = await Client.ProbeReceiverCapabilitiesAsync(Token);
            return capabilities.ToString();
            // <-----------
        }

        private async Task<string> ListInputsAsync()
        {
            var inputs = await Client.RefreshInputsAsync(Token);
            return inputs.Count == 0 ? Loc.Get("The receiver returned no inputs.") : string.Join(Environment.NewLine, inputs);
            // <-----------
        }

        private async Task<string> ZonePowerAsync(CommandInput input)
        {
            var zone = ParseZone(input.Parameter1);
            var switchOn = ParseOnOff(input.Parameter2);
            var response = zone == 2
                ? await Telnet.SetZone2PowerAsync(switchOn, Token)
                : await Telnet.SetZone3PowerAsync(switchOn, Token);
            return Loc.Format("Telnet response: {0}", response);
            // <-----------
        }

        private async Task<string> ZoneVolumeStepAsync(CommandInput input)
        {
            var zone = ParseZone(input.Parameter1);
            var increase = ParseUpDown(input.Parameter2);
            Task command = (zone, increase) switch
            {
                (2, true) => Client.Zone2VolumeUpAsync(input.Protocol, Token),
                (2, false) => Client.Zone2VolumeDownAsync(input.Protocol, Token),
                (_, true) => Client.Zone3VolumeUpAsync(input.Protocol, Token),
                _ => Client.Zone3VolumeDownAsync(input.Protocol, Token)
            };

            await command;
            return CommandSentText;
            // <-----------
        }

        private async Task<string> ZoneSetVolumeAsync(CommandInput input)
        {
            var zone = ParseZone(input.Parameter1);
            var volumeDb = ParseDouble(input.Parameter2);
            await (zone == 2
                ? Client.SetZone2VolumeDbAsync(volumeDb, input.Protocol, Token)
                : Client.SetZone3VolumeDbAsync(volumeDb, input.Protocol, Token));
            return CommandSentText;
            // <-----------
        }

        private async Task<string> ZoneMuteAsync(CommandInput input)
        {
            var zone = ParseZone(input.Parameter1);
            var muted = ParseOnOff(input.Parameter2);
            await (zone == 2
                ? Client.SetZone2MuteAsync(muted, input.Protocol, Token)
                : Client.SetZone3MuteAsync(muted, input.Protocol, Token));
            return CommandSentText;
            // <-----------
        }

        private async Task<string> ZoneInputAsync(CommandInput input)
        {
            var zone = ParseZone(input.Parameter1);
            var response = zone == 2
                ? await Telnet.SetZone2InputAsync(input.Parameter2, Token)
                : await Telnet.SetZone3InputAsync(input.Parameter2, Token);
            return Loc.Format("Telnet response: {0}", response);
            // <-----------
        }

        private async Task<string> ShowConfiguredSpeakersAsync()
        {
            var speakers = await Client.GetConfiguredSpeakersAsync(Token);
            if (speakers.Count == 0)
            {
                return Loc.Get("The receiver reported no configured speakers.");
                // <-----------
            }

            var builder = new StringBuilder();
            builder.AppendLine($"{Loc.Get("Index"),5}  {Loc.Get("Channel"),-24} {Loc.Get("Level"),10}  {Loc.Get("Distance"),10}");
            foreach (var speaker in speakers)
            {
                builder.AppendLine(
                    $"{speaker.SpeakerIndex,5}  {speaker.Channel,-24} " +
                    $"{FormatDecibels(speaker.LevelDb),10}  {FormatMeters(speaker.DistanceMeters),10}");
            }

            return builder.ToString();
            // <-----------
        }

        private async Task<string> ShowPresetLevelsAsync()
        {
            var levels = await Client.GetSpeakerPresetLevelsAsync(Token);
            if (levels.Count == 0)
            {
                return Loc.Get("The receiver returned no preset levels.");
                // <-----------
            }

            var unknownText = Loc.Get("Unknown");
            var builder = new StringBuilder();
            builder.AppendLine($"{Loc.Get("Index"),5}  {Loc.Get("Channel"),-42} {Loc.Get("Level"),10}");
            foreach (var level in levels)
            {
                builder.AppendLine(
                    $"{level.SpeakerIndex,5}  {level.Channel?.ToString() ?? unknownText,-42} {FormatDecibels(level.Decibels),10}");
            }

            return builder.ToString();
            // <-----------
        }

        private async Task<string> ShowDistancesAsync()
        {
            var configuration = await Client.GetSpeakerDistancesAsync(Token);
            var unknownText = Loc.Get("Unknown");
            var builder = new StringBuilder();
            builder.AppendLine(Loc.Format("Receiver unit: {0}, step: {1}", configuration.ReceiverUnit, FormatMeters(configuration.Step)));
            builder.AppendLine($"{Loc.Get("Index"),5}  {Loc.Get("Channel"),-24} {Loc.Get("Distance"),10}");
            foreach (var speaker in configuration.Speakers)
            {
                builder.AppendLine(
                    $"{speaker.SpeakerIndex,5}  {speaker.Channel?.ToString() ?? unknownText,-24} {FormatMeters(speaker.Meters),10}");
            }

            return builder.ToString();
            // <-----------
        }

        private async Task<string> ReadActivePresetAsync()
        {
            var preset = await Client.GetActiveSpeakerPresetAsync(Token);
            return Loc.Format("Active speaker preset: {0}", preset);
            // <-----------
        }

        private async Task<string> SelectPresetAsync(CommandInput input)
        {
            var preset = ParseInt(input.Parameter1);
            await Client.SelectSpeakerPresetAsync(preset, cancellationToken: Token);
            return Loc.Format("Preset {0} confirmed by the receiver.", preset);
            // <-----------
        }

        private async Task<string> ShowChannelLevelsAsync()
        {
            var levels = await Client.GetSpeakerLevelsAsync(DenonControlProtocol.Telnet, Token);
            if (levels.Count == 0)
            {
                return Loc.Get("The receiver returned no configured channel levels.");
                // <-----------
            }

            var builder = new StringBuilder();
            foreach (var level in levels)
            {
                var levelText = level.IsOff ? "OFF" : FormatDecibels(level.Decibels);
                builder.AppendLine($"{level.Channel,-24} {levelText,10}   ({level.RawResponse})");
            }

            return builder.ToString();
            // <-----------
        }

        private async Task<string> SetChannelLevelAsync(CommandInput input)
        {
            var channel = ParseChannel(input.Parameter1);
            var decibels = ParseDouble(input.Parameter2);
            await Client.SetSpeakerLevelAsync(channel, decibels, DenonControlProtocol.Telnet, Token);
            return Loc.Format("{0}: {1} set.", channel, FormatDecibels(decibels));
            // <-----------
        }

        // ---------------------------------------------------------------
        // Formatting and parsing helpers
        // ---------------------------------------------------------------

        private static void AppendZone(StringBuilder builder, string name, DenonZoneState? zone)
        {
            if (zone is not null)
            {
                builder.AppendLine(
                    Loc.Format(
                        "{0}:       {1}, {2}, {3}, Mute {4}",
                        name,
                        zone.Power,
                        zone.Input ?? Loc.Get("unknown"),
                        FormatDecibels(zone.VolumeDb),
                        FormatMute(zone.IsMuted)));
            }
        }

        private static string FormatActiveSpeakers(DenonAudioInfo? audio) =>
            audio is null || audio.ActiveSpeakers.Count == 0
                ? Loc.Get("unknown/none")
                : string.Join(", ", audio.ActiveSpeakers);

        private static string FormatDecibels(double? value) =>
            value is null ? "-" : $"{value.Value.ToString("0.0", CultureInfo.InvariantCulture)} dB";

        private static string FormatMeters(double? value) =>
            value is null ? "-" : $"{value.Value.ToString("0.00", CultureInfo.InvariantCulture)} m";

        private static string FormatMute(bool? muted) => muted switch
        {
            true => Loc.Get("On"),
            false => Loc.Get("Off"),
            null => Loc.Get("unknown")
        };

        private static double ParseDouble(string text)
        {
            var trimmed = text.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.GetCultureInfo("de-DE"), out var value) ||
                double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
                // <-----------
            }

            throw new FormatException(Loc.Format("'{0}' is not a valid number.", text));
            // <-----------
        }

        private static int ParseInt(string text)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
                // <-----------
            }

            throw new FormatException(Loc.Format("'{0}' is not a valid integer.", text));
            // <-----------
        }

        private static int ParseZone(string text)
        {
            var trimmed = text.Trim();
            if (trimmed is "2" or "3")
            {
                return int.Parse(trimmed, CultureInfo.InvariantCulture);
                // <-----------
            }

            throw new ArgumentException(Loc.Get("The zone must be 2 or 3."));
            // <-----------
        }

        private static bool ParseOnOff(string text) => text.Trim().ToUpperInvariant() switch
        {
            "ON" => true,
            "OFF" => false,
            _ => throw new ArgumentException(Loc.Get("Enter 'on' or 'off'."))
            // <-----------
        };

        private static bool ParseUpDown(string text) => text.Trim().ToUpperInvariant() switch
        {
            "UP" => true,
            "DOWN" => false,
            _ => throw new ArgumentException(Loc.Get("Enter 'up' or 'down'."))
            // <-----------
        };

        private static DenonSpeakerLevelChannel ParseChannel(string text)
        {
            if (Enum.TryParse<DenonSpeakerLevelChannel>(text.Trim(), true, out var channel) &&
                Enum.IsDefined(typeof(DenonSpeakerLevelChannel), channel))
            {
                return channel;
                // <-----------
            }

            throw new ArgumentException(Loc.Format("'{0}' is not a known channel name.", text));
            // <-----------
        }

        private void BtnOpenWebPage_Click(object sender, RoutedEventArgs eventArgs)
        {
            var host = _client?.Host ?? TxtHost.Text.Trim();
            if (host.Length == 0)
            {
                TxtResult.Text = Loc.Get("Enter a host or IP address.");
                return;
                // <-----------
            }

            try
            {
                var webPageUri = new UriBuilder(Uri.UriSchemeHttp, host).Uri;
                Process.Start(new ProcessStartInfo(webPageUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception) when (
                exception is Win32Exception or
                InvalidOperationException or
                ArgumentException or
                FormatException)
            {
                TxtResult.Text = $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        private void BtnSupportReport_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (_client is null || _telnetClient is null)
            {
                return;
                // <-----------
            }

            var reportWindow = new SupportReportWindow(this, _client, _telnetClient, _detectedProfileId, _enteredReceiverType);
            reportWindow.ShowDialog();
        }

        private void LstCommands_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs eventArgs)
        {
            if (eventArgs.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(LstCommands, source) is not ListBoxItem)
            {
                return;
                // <-----------
            }

            if (LstCommands.SelectedItem is not TesterCommand command ||
                command.Parameters.Count > 0 ||
                !BtnSend.IsEnabled)
            {
                return;
                // <-----------
            }

            BtnSend_Click(BtnSend, eventArgs);
        }
    }

    internal sealed record CommandParameter(string Label, string DefaultValue);

    internal sealed record CommandInput(string Parameter1, string Parameter2, DenonControlProtocol Protocol);

    internal sealed record TesterCommand(
        string Group,
        string Name,
        bool UsesProtocol,
        IReadOnlyList<CommandParameter> Parameters,
        Func<CommandInput, Task<string>> ExecuteAsync);
}