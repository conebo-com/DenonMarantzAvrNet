using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DenonMarantzAvrNet.Transport;
using DenonMarantzAvrNet.Models;
using DenonMarantzAvrNet.Profiles;
using DenonMarantzAvrNet.Discovery;

namespace DenonMarantzAvrNet.Tests;

/// <summary>
/// Offline regression checks for read-only speaker discovery. No receiver or NuGet
/// test-runner package required. Run on Windows with the .NET Framework 4.8 pack.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            TestLevelIndicesAreNative();
            TestDistancesAreIndependent();
            TestConfigurationAndMenu();
            TestLayoutIsNotAChannelList();
            TestXmlRejectsUnsafeAndAmbiguousResponses();
            TestReadOnlyTransportAndPartialFailures();
            TestTelnetResponseCorrelation();
            TestTelnetLoopbackSession();
            TestReceiverProfileSelection();
            TestAppCommandResponseClassification();
            TestAppCommandPort80Discovery();
            TestLegacyInspectionWithoutLeakingValues();
            TestLegacyEndpointProbeIsReadOnly();
            TestNativeSpeakerMatrixParsing();
            TestNativeSpeakerMatrixHttpPort80();
            TestDynamicDeviceCapabilities();
            TestReadCommandClassification();
            TestReadOnlyCapabilityHttpProbes();
            TestChannelAdjustmentsAreNotCalibration();
            TestSoundFamiliesFromMainZoneShortcuts();
            TestOpsmlallParser();
            TestDistanceUnitConversion();
            TestLegacyCalibrationValues();
            TestLegacyCalibrationRejectsBadPages();
            TestLegacyCalibrationGetOnly();
            Console.WriteLine("Discovery/transport: 25 tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private const string LegacyLevelsFixture = @"<!DOCTYPE html><html><body>
      <FORM name='spsetup' action='s_speakersetup.asp' method='POST'>
      <div>Speakers/Levels</div>
      <input type='hidden' name='setPureDirectOn' value='OFF'>
      <tr><td><B>Front L</B></td><td><input id='RangeCVFL' type='range' value='-6.0' />
          <input type='hidden' name='textCVFL' value='-6.0'></td></tr>
      <tr><td><b>Top Middle L</b></td><td><input type='hidden' name='textCVTML' value='-2.0'></td></tr>
      <tr><td><b>Surr. Back</b></td><td><input type='hidden' name='textCVSB' value='-8.5'></td></tr>
      <tr><td><b>Future channel</b></td><td><input type='hidden' name='textCVXYZ' value='1.5'></td></tr>
      </FORM></body></html>";

    private const string LegacyDistancesFixture = @"<!DOCTYPE html><html><body>
      <FORM name='spsetup' action='s_speakersetup.asp' method='POST'>
      <div>Speakers/Distances</div>
      <input type='hidden' name='setSetupLock' value='OFF'>
      <input type='radio' name='radioDelayTimeMode' value='M'>Meters
      <input type='radio' name='radioDelayTimeMode' value='F' checked>Feet
      <tr><td><b>Front L</b></td><td><input type='text' name='textDelayTimeFl' value='9.71'></td></tr>
      <tr><td><b>Top Middle L</b></td><td><input type='text' name='textDelayTimeTML' value='5.00'></td></tr>
      <tr><td><b>Future channel</b></td><td><input type='text' name='textDelayTimeXYZ' value='2.00'></td></tr>
      </FORM></body></html>";

    private static void TestLegacyCalibrationValues()
    {
        var levels = DenonLegacySpeakerCalibrationReader.ParseLevels(LegacyLevelsFixture);
        Check(levels.Count == 4 && levels[0].NativeChannelCode == "FL" &&
            levels[0].Decibels == -6.0 && levels[1].Channel == SpeakerChannel.TopMiddleLeft &&
            levels[2].Decibels == -8.5 && levels[3].Channel == null &&
            levels[3].NativeChannelCode == "XYZ",
            "HTML speaker calibration levels must preserve native codes, unknown channels and signed dB.");
        var distance = DenonLegacySpeakerCalibrationReader.ParseDistances(LegacyDistancesFixture);
        Check(distance.Unit == DenonSpeakerDistanceUnit.Feet && distance.Speakers.Count == 3 &&
            Math.Abs(distance.Speakers[0].Meters - 2.959608) < 0.000001 &&
            distance.Speakers[1].FormFieldName == "textDelayTimeTML" &&
            distance.Speakers[2].Channel == null,
            "Legacy HTML distance values must be dynamically enumerated and feet converted to meters.");
        var meters = DenonLegacySpeakerCalibrationReader.ParseDistances(
            LegacyDistancesFixture.Replace("value='M'>Meters", "value='M' checked>Meters")
                .Replace("value='F' checked>Feet", "value='F'>Feet"));
        Check(meters.Unit == DenonSpeakerDistanceUnit.Meters &&
            Math.Abs(meters.Speakers[0].Meters - 9.71) < 0.000001,
            "Receiver unit selection must be interpreted before converting distances.");
    }

    private static void TestLegacyCalibrationRejectsBadPages()
    {
        ExpectInvalid(() => DenonLegacySpeakerCalibrationReader.ParseLevels(
            "<html><frameset><frame src='d_speakersetup.asp'></frameset></html>"));
        ExpectInvalid(() => DenonLegacySpeakerCalibrationReader.ParseLevels(
            LegacyLevelsFixture.Replace("value='-6.0'", "value='-20.0'")));
        ExpectInvalid(() => DenonLegacySpeakerCalibrationReader.ParseLevels(
            LegacyLevelsFixture.Replace("</FORM>", "<tr><td>Duplicate</td><td>" +
                "<input type='hidden' name='textCVFL' value='0'></td></tr></FORM>")));
        ExpectInvalid(() => DenonLegacySpeakerCalibrationReader.ParseDistances(
            LegacyDistancesFixture.Replace("value='F' checked", "value='F'")));
        ExpectInvalid(() => DenonLegacySpeakerCalibrationReader.ParseDistances(
            LegacyDistancesFixture.Replace("value='OFF'", "value='ON'")));
    }

    private static void TestLegacyCalibrationGetOnly()
    {
        var handler = new FakeLegacyCalibrationHandler();
        using (var transport = new DenonHttpTransport(handler, TimeSpan.FromSeconds(2)))
        {
            var read = DenonLegacySpeakerCalibrationReader.ReadAsync(
                "receiver.example", 80, transport, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Paths.SequenceEqual(new[] {
                    DenonLegacySpeakerCalibrationReader.LevelsPath,
                    DenonLegacySpeakerCalibrationReader.DistancesPath
                }) && handler.Methods.All(x => x == HttpMethod.Get),
                "Legacy calibration discovery must never submit the HTML setup form or issue POST requests.");
            Check(read.HttpPort == 80 && read.Levels.Count == 4 && read.Distances.Count == 3,
                "Legacy calibration reader must obtain both setup pages without a model profile.");
        }
    }

    private sealed class FakeLegacyCalibrationHandler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = new List<string>();
        internal List<HttpMethod> Methods { get; } = new List<HttpMethod>();
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Methods.Add(request.Method);
            var response = Paths[Paths.Count - 1] == DenonLegacySpeakerCalibrationReader.LevelsPath
                ? LegacyLevelsFixture : LegacyDistancesFixture;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(response) });
        }
    }

    private static void TestChannelAdjustmentsAreNotCalibration()
    {
        const string xml = "<rx><cmd><status>1</status><chlists>" +
            "<ch><name>FL</name><status>1</status><sptype>1</sptype><level>24</level><value>0</value></ch>" +
            "<ch><name>TML</name><status>0</status><level>25</level><value>1</value></ch>" +
            "</chlists></cmd></rx>";
        var values = DenonChannelLevelAdjustmentReader.Parse(xml);
        Check(values.Count == 2 && values[0].Channel == "FL" &&
            values[0].LevelRaw == "24" && values[1].ValueRaw == "1",
            "GetChLevel must preserve raw per-input fields and must not infer calibration dB.");
        try
        {
            DenonChannelLevelAdjustmentReader.Parse("<rx><error>Unsupported</error></rx>");
            throw new Exception("Unusable command must not pass as successful calibration.");
        }
        catch (InvalidDataException) { }
    }

    private static void TestSoundFamiliesFromMainZoneShortcuts()
    {
        const string xml = "<Device_Info><DeviceZoneCapabilities><Zone><No>0</No></Zone>" +
            "<ShortcutControl><EntryList>" +
            "<Shortcut><Category>5</Category><FuncName>MOVIE</FuncName></Shortcut>" +
            "<Shortcut><Category>5</Category><FuncName>MUSIC</FuncName></Shortcut>" +
            "<Shortcut><Category>5</Category><FuncName>GAME</FuncName></Shortcut>" +
            "<Shortcut><Category>5</Category><FuncName>PURE</FuncName></Shortcut>" +
            "<Shortcut><Category>4</Category><FuncName>GAME</FuncName></Shortcut>" +
            "</EntryList></ShortcutControl></DeviceZoneCapabilities></Device_Info>";
        var catalog = DenonDeviceCapabilityReader.Parse(xml, 80);
        Check(catalog.SoundModeFamilies.Count == 4 &&
            catalog.SoundModeFamilies.SequenceEqual(new[] { "MOVIE", "MUSIC", "GAME", "PURE" }),
            "Remote sound families should be read from main zone, not confused with GAME input.");
    }

    private static void TestOpsmlallParser()
    {
        var list = DenonSoundModeListReader.Parse(new[] {
            "OPSMLALL MOV011Stereo",
            "OPSMLALL MUS020Dolby Audio - Dolby Surround",
            "OPSMLALL PUR031Pure Direct",
            "OPSMLALL END"
        });
        Check(list.Count == 3 && list[0].FamilyCode == "MOV" && list[0].IsActive &&
            !list[1].IsActive && list[2].DisplayName == "Pure Direct",
            "OPSMLALL must retain category, active status and raw label without inventing set commands.");
        Check(DenonTelnetResponseMatcher.IsResponse("OPSMLALL?", "OPSMLALL PUR031Pure Direct") &&
            DenonTelnetResponseMatcher.IsResponse("MSMOVIE", "MSSTEREO"),
            "Query/selection replies must be matched by their command family.");
        try
        {
            DenonSoundModeListReader.Parse(new[] { "OPSMLALL MOV011Stereo" });
            throw new Exception("Incomplete OPSMLALL must not be treated as a supported full list.");
        }
        catch (InvalidDataException) { }
    }

    private static void TestDistanceUnitConversion()
    {
        var feet = AjaxSpeakerDistanceProvider.RawToMeters(971, DenonSpeakerDistanceUnit.Feet);
        Check(Math.Abs(feet - 2.959608) < 0.00001,
            "Unit=1 must be feet, not meters; this is critical for safe distance writes.");
        Check(Math.Abs(AjaxSpeakerDistanceProvider.RawToMeters(296, DenonSpeakerDistanceUnit.Meters) - 2.96) < .00001,
            "Unit=2 must be meters.");
        Check(AjaxSpeakerDistanceProvider.MetersToRaw(2.959608, DenonSpeakerDistanceUnit.Feet) == 971 &&
            AjaxSpeakerDistanceProvider.MetersToRaw(2.96, DenonSpeakerDistanceUnit.Meters) == 296,
            "Read/write distance scaling must round-trip in selected receiver units.");
    }

    private static void TestLevelIndicesAreNative()
    {
        const string xml = "<Levels mode='normal'><List>" +
            "<Speaker index='0' display='3'>20</Speaker>" +
            "<Speaker index='30' display='3'>-25</Speaker>" +
            "</List></Levels>";
        var entries = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            xml, "Levels", "Speaker", "avc-x6800h", true);
        Check(entries.Length == 2 && entries[0].NativeIndex == 0 &&
              entries[1].NativeIndex == 30, "Native level indices must be preserved.");
        Check(entries[1].RawValue == "-25" && entries[1].DisplayCode == 3,
            "Raw value and display code must be preserved.");
        Check(entries[1].ReceiverLabel == null,
            "A mapped X6800H channel must NOT be claimed as a receiver label.");
        Check(entries[0].ProfileChannelHint == SpeakerChannel.FrontLeft,
            "X6800H profile mapping is exposed only as a hint.");
        var unknownModel = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            xml, "Levels", "Speaker", "avc-x6700h", true);
        Check(unknownModel[0].ProfileChannelHint == null,
            "Mappings must not be silently reused for other models.");
    }

    private static void TestDistancesAreIndependent()
    {
        const string xml = "<Distances><Unit>1</Unit><Step>1</Step><List>" +
            "<Speaker index='0'>315</Speaker><Speaker index='1' name='FR'>285</Speaker>" +
            "</List></Distances>";
        var entries = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            xml, "Distances", "Speaker", "avc-x6800h", false);
        var (unit, step) = DenonSpeakerDiscoveryParser.ParseDistanceMetadata(xml);
        Check(entries.Length == 2 && entries[1].ReceiverLabel == "FR",
            "Native receiver labels, when present, must survive parsing.");
        Check(entries[1].ProfileChannelHint == SpeakerChannel.FrontRight,
            "Distance and level indices must use different mappings.");
        Check(unit == "1" && step == "1",
            "Unit/step codes must not be interpreted without model verification.");
    }

    private static void TestConfigurationAndMenu()
    {
        const string configuration = "<SpeakerConfig>" +
            "<Speaker index='0'><Value>2</Value><List>2</List><List>3</List></Speaker>" +
            "<Speaker index='2'><Value>6</Value><List>1</List></Speaker>" +
            "</SpeakerConfig>";
        var entries = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            configuration, "SpeakerConfig", "Speaker", "avc-x6800h", null);
        Check(entries.Length == 2 && entries[0].RawValue == "2" &&
            entries[1].NativeIndex == 2 && entries[0].ProfileChannelHint == null,
            "Config speaker indices are a separate namespace and list options are not speaker indices.");
        var menu = DenonSpeakerDiscoveryParser.ParseMenu(
            "<listSetupMenu><AmpAssign>3</AmpAssign><SpeakerConfig>2</SpeakerConfig></listSetupMenu>");
        Check(menu.Length == 2 && menu[1].RawStatus == "2",
            "Menu accessibility status must be kept as a raw code.");
    }

    private static void TestLayoutIsNotAChannelList()
    {
        const string xml = "<SpeakerLayout><List>" +
            "<Item index='7' display='3'><Config>1</Config><Conn>2</Conn></Item>" +
            "<Item index='14' display='1'><Config>0</Config><Conn>0</Conn></Item>" +
            "</List></SpeakerLayout>";
        var entries = DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            xml, "", "Item", "avc-x6800h", null);
        Check(entries.Length == 2 && entries[1].NativeIndex == 14 &&
            entries[0].RawValue.Contains("Conn=2") && entries[0].ProfileChannelHint == null,
            "Layout Item indices describe groups, not channels.");
    }

    private static void TestXmlRejectsUnsafeAndAmbiguousResponses()
    {
        ExpectInvalid(() => DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            "<Levels><List><Speaker index='0'>1</Speaker><Speaker index='0'>2</Speaker></List></Levels>",
            "Levels", "Speaker", "avc-x6800h", true));
        ExpectInvalid(() => DenonSpeakerDiscoveryParser.ParseSpeakerEntries(
            "<Levels><List><Speaker index='invalid'>1</Speaker></List></Levels>",
            "Levels", "Speaker", "avc-x6800h", true));
        try
        {
            DenonSpeakerDiscoveryParser.ParseMenu(
                "<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///C:/secret.txt'>]>" +
                "<listSetupMenu><A>&external;</A></listSetupMenu>");
            throw new Exception("DTD must be rejected.");
        }
        catch (System.Xml.XmlException) { }
    }

    private static void TestReadOnlyTransportAndPartialFailures()
    {
        var handler = new FakeSpeakerHandler();
        using (var transport = new DenonHttpTransport(handler, TimeSpan.FromSeconds(2)))
        {
            var report = DenonSpeakerDiscoveryReader.ReadAsync(
                new DenonProfileContext("receiver.example", transport),
                DenonSpeakerAjaxOptions.AvcX6800H,
                "avc-x6800h",
                CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Requests.Count == 5 &&
                handler.Requests.All(r => r.Method == HttpMethod.Get &&
                    r.RequestUri!.AbsolutePath == "/ajax/speakers/get_config"),
                "Discovery must only send five read-only GET requests.");
            Check(report.LevelEntries.Count == 1 && report.DistanceEntries.Count == 1,
                "Primary index results must survive an optional endpoint failure.");
            Check(report.ConfigurationEntries.Count == 0 && report.Warnings.Count == 1 &&
                  report.Warnings[0].Contains("type=3"),
                "Unsupported optional page must be reported, not treated as global unsupported.");
        }
    }

    private static void TestTelnetResponseCorrelation()
    {
        // Real traces from an X6800H: unsolicited status must never be reported
        // as the response to an unrelated command, including the MVMAX line.
        Check(!DenonTelnetResponseMatcher.IsResponse("PW?", "DCAUTO"), "PW? mismatch");
        Check(DenonTelnetResponseMatcher.IsResponse("PW?", "PWON"), "PW? match");
        Check(!DenonTelnetResponseMatcher.IsResponse("MU?", "MVMAX 755"), "MU? mismatch");
        Check(!DenonTelnetResponseMatcher.IsResponse("MV?", "MVMAX 755"), "MV? must ignore MVMAX");
        Check(DenonTelnetResponseMatcher.IsResponse("MV?", "MV60"), "MV? match");
        Check(!DenonTelnetResponseMatcher.IsResponse("SI?", "MUOFF"), "SI? mismatch");
        Check(DenonTelnetResponseMatcher.IsResponse("SI?", "SIMPLAY"), "SI? match");
        Check(DenonTelnetResponseMatcher.IsResponse("MS?", "MSSTEREO"), "MS? match");
        Check(!DenonTelnetResponseMatcher.IsResponse("MS?", "SVOFF"), "MS? mismatch");
        Check(DenonTelnetResponseMatcher.IsResponse("CV?", "CVEND"), "CV? terminator");
        Check(DenonTelnetResponseMatcher.IsResponse("CVFL?", "CVFL 50"), "CVFL? match");
        Check(!DenonTelnetResponseMatcher.IsResponse("CVFL?", "CVFR 50"), "CVFL? mismatch");
    }

    private static void TestTelnetLoopbackSession()
    {
        // A local fake receiver sends unsolicited status lines before the
        // requested response. Commands must share ONE connection and the
        // parser must wait for the correct result or CVEND terminator.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using (var client = await listener.AcceptTcpClientAsync())
                using (var stream = client.GetStream())
                {
                    var query = await ReadAsciiCommandAsync(stream);
                    Check(query == "PW?", "First request must be PW?.");
                    await WriteLinesAsync(stream, "DCAUTO", "PWON");

                    query = await ReadAsciiCommandAsync(stream);
                    Check(query == "MV?", "Second request must reuse the same TCP connection.");
                    await WriteLinesAsync(stream, "MVMAX 755", "MUOFF", "MV60");

                    query = await ReadAsciiCommandAsync(stream);
                    Check(query == "CV?", "Third request must reuse the same TCP connection.");
                    await WriteLinesAsync(stream, "SIMPLAY", "CVFL 50", "CVFR 50", "CVEND");
                }
            });

            var session = DenonTelnetSession.ForEndpoint("127.0.0.1", port);
            var timeout = TimeSpan.FromSeconds(5);
            var pw = session.ExecuteAsync("PW?", null, timeout, CancellationToken.None)
                .GetAwaiter().GetResult();
            var mv = session.ExecuteAsync("MV?", null, timeout, CancellationToken.None)
                .GetAwaiter().GetResult();
            var cv = session.ExecuteAsync("CV?", "CVEND", timeout, CancellationToken.None)
                .GetAwaiter().GetResult();
            Check(pw.SequenceEqual(new[] { "PWON" }) && mv.SequenceEqual(new[] { "MV60" }),
                "Unsolicited status and MVMAX must not be used as command replies.");
            Check(cv.SequenceEqual(new[] { "CVFL 50", "CVFR 50", "CVEND" }),
                "CV? must collect only channel lines through CVEND.");
            server.GetAwaiter().GetResult();
        }
        finally { listener.Stop(); }
    }

    private static async Task<string> ReadAsciiCommandAsync(NetworkStream stream)
    {
        var data = new byte[1];
        var result = new StringBuilder();
        while (await stream.ReadAsync(data, 0, 1) > 0)
        {
            if (data[0] == 13 || data[0] == 10) return result.ToString();
            result.Append((char)data[0]);
        }
        throw new IOException("Fake receiver did not receive command terminator.");
    }

    private static async Task WriteLinesAsync(NetworkStream stream, params string[] lines)
    {
        var payload = Encoding.ASCII.GetBytes(string.Join("\r", lines) + "\r");
        await stream.WriteAsync(payload, 0, payload.Length);
        await stream.FlushAsync();
    }

    private static void TestReceiverProfileSelection()
    {
        Check(DenonReceiverProfileRegistry.FindById("legacy-goform")?.Id == "legacy-goform",
            "Legacy receiver profile must be selectable.");
        Check(DenonReceiverProfileRegistry.FindById("unknown")?.Id == "unknown",
            "Generic profile must be selectable.");
        var device = new DenonDeviceInfo("*AVR-X4100W", "*AVR-X4100W", "0300", 3, null, null);
        Check(DenonReceiverProfileRegistry.Select(device, 80).Id == "legacy-goform",
            "X4100W must not be assigned an X6700H speaker AJAX profile.");
    }

    private static void TestAppCommandResponseClassification()
    {
        Check(DenonXmlParser.HasAppCommandData(
            "<rx><cmd id='1'><zone1>ON</zone1></cmd></rx>"),
            "Nonempty AppCommand reply must count as available.");
        Check(!DenonXmlParser.HasAppCommandData("<rx/>"),
            "Empty HTTP success must not imply AppCommand support.");
        Check(!DenonXmlParser.HasAppCommandData("<rx><error>not supported</error></rx>"),
            "Receiver error must not imply AppCommand support.");
    }

    private static void TestAppCommandPort80Discovery()
    {
        using (var transport = new DenonHttpTransport(new FakeAppCommandHandler(true), TimeSpan.FromSeconds(2)))
        using (var receiver = new DenonAvrClient("receiver.example", transport))
        {
            receiver.InitializeAsync().GetAwaiter().GetResult();
            Check(receiver.HttpPort == 80 && receiver.ReceiverCapabilities?.SupportsAppCommand == true,
                "AppCommand on port 80 must be detected from XML response, not the port.");
            Check(receiver.ReceiverProfileId == "legacy-goform",
                "Port 80 AppCommand does not imply a new speaker AJAX profile.");
        }
        using (var transport = new DenonHttpTransport(new FakeAppCommandHandler(false), TimeSpan.FromSeconds(2)))
        using (var receiver = new DenonAvrClient("receiver.example", transport))
        {
            receiver.InitializeAsync().GetAwaiter().GetResult();
            Check(receiver.ReceiverCapabilities?.SupportsAppCommand == false,
                "A missing AppCommand result on both ports must be classified unavailable.");
        }
        using (var transport = new DenonHttpTransport(new FakeAppCommandHandler(true, 8080), TimeSpan.FromSeconds(2)))
        using (var receiver = new DenonAvrClient("receiver.example", transport))
        {
            receiver.InitializeAsync().GetAwaiter().GetResult();
            Check(receiver.HttpPort == 80 && receiver.AppCommandPort == 8080 &&
                  receiver.ReceiverCapabilities?.SupportsAppCommand == true,
                "AppCommand may be on port 8080 even when Deviceinfo responded on 80.");
        }
    }

    private static void TestLegacyInspectionWithoutLeakingValues()
    {
        var xml = DenonLegacyHttpResponseInspector.Inspect("Device info", "GET",
            "/goform/Deviceinfo.xml",
            "<Device_Info><MacAddress>00:11:22:33:44:55</MacAddress>" +
            "<ModelName>AVR-X4100W</ModelName></Device_Info>");
        Check(xml.Result == "Received" && xml.Root == "Device_Info" &&
              xml.ElementPaths.Any(p => p.EndsWith("MacAddress")) &&
              !string.Join(" ", xml.ElementPaths).Contains("00:11:22"),
            "Legacy diagnostic must retain XML field names but never device values.");

        var html = DenonLegacyHttpResponseInspector.Inspect("Web", "GET", "/",
            "<html><a href='/Setup/Speakers.html'>Speakers</a>" +
            "<a href='/goform/formiPhoneAppDirect.xml?MVUP'>Unsafe GET</a>" +
            "<script src='/js/setup.js'></script></html>");
        Check(html.Result == "Received" && html.Root == "HTML" &&
              html.DiscoveredLinks.Contains("/Setup/Speakers.html") &&
              !html.DiscoveredLinks.Any(x => x.Contains("?MVUP")),
            "References can be inventoried but command links must never be fetched.");
        var empty = DenonLegacyHttpResponseInspector.Inspect("Status", "POST", "/goform/AppCommand.xml", "<rx/>");
        Check(empty.Result == "Rejected", "Empty AppCommand must not mean capability support.");
        var unsafeXml = DenonLegacyHttpResponseInspector.Inspect("Unsafe", "GET", "/",
            "<!DOCTYPE doc [<!ENTITY x SYSTEM 'file:///C:/private.txt'>]><doc>&x;</doc>");
        Check(unsafeXml.Result == "Unparseable", "External entities must not be expanded.");
    }

    private static void TestLegacyEndpointProbeIsReadOnly()
    {
        var handler = new FakeLegacyDiscoveryHandler();
        using (var transport = new DenonHttpTransport(handler, TimeSpan.FromSeconds(5)))
        {
            var report = DenonLegacyHttpDiscoveryReader.ReadAsync(
                "receiver.example", 80, transport, CancellationToken.None).GetAwaiter().GetResult();
            Check(report.Probes.Count == 10 && handler.Requests.Count == 10,
                "All known read-only legacy probes should be attempted.");
            Check(handler.Requests.All(r => r.Method == HttpMethod.Get || r.Method == HttpMethod.Post),
                "Only known read-only HTTP endpoints and status POSTs may be used.");
            Check(!handler.Requests.Any(r => r.RequestUri!.AbsolutePath.Contains("set_config") ||
                r.RequestUri.Query.Contains("MVUP")),
                "Discovery must never issue setup writes or command URLs.");
            Check(report.Probes.Any(x => x.Name == "Legacy MainZone XML" && x.Result == "Received"),
                "Legacy MainZone status must remain available independently of profiles.");
        }
    }

    private static void TestNativeSpeakerMatrixParsing()
    {
        const string xml = "<rx><cmd id='3'><name>GetActiveSpeaker</name><list>" +
            "<param name='activespa1' control='0'></param>" +
            "<param name='activespb1' control='2'>SW</param>" +
            "<param name='activespc2' control='1'>C</param>" +
            "<param name='activespd2' control='5'>XYZ</param>" +
            "</list></cmd></rx>";
        var result = DenonSpeakerMatrixDiscoveryReader.Parse(xml, 80);
        Check(result.Status == "Received" && result.HttpPort == 80 &&
              result.Slots.Count == 4, "Native matrix should preserve all reported slots.");
        Check(result.Slots[0].NativeId == "activespa1" && result.Slots[0].Label == null &&
              result.Slots[0].ControlCode == "0", "Blank slots must be retained.");
        Check(result.Slots[1].Label == "SW" && result.Slots[2].Label == "C" &&
              result.Slots[3].ControlCode == "5" && result.Slots[3].Label == "XYZ",
            "Raw labels and future control codes must not be rewritten.");
        Check(DenonSpeakerMatrixDiscoveryReader.Parse("<rx><error>unknown</error></rx>", 80)
              .Status == "Rejected", "Explicit error must be separated from empty answers.");
        Check(DenonSpeakerMatrixDiscoveryReader.Parse("<rx><cmd><name>GetActiveSpeaker</name>" +
              "<list/></cmd></rx>", 80).Status == "Empty",
            "A blank matrix is not proof that the receiver has no speakers.");
        Check(DenonSpeakerMatrixDiscoveryReader.Parse(null, null).Status == "NoResponse",
            "Timeout/no response must not be called unsupported.");
        Check(DenonSpeakerMatrixDiscoveryReader.Parse("<rx><cmd><name>GetOtherCommand</name>" +
              "<list><param name='activespa1' control='2'>FL</param></list></cmd></rx>", 80)
              .Slots.Count == 0, "Never accept slots from another command.");
        try
        {
            DenonSpeakerMatrixDiscoveryReader.Parse(
                "<!DOCTYPE rx [<!ENTITY x SYSTEM 'file:///private'>]><rx>&x;</rx>", 80);
            throw new Exception("Unsafe XML must be rejected.");
        }
        catch (System.Xml.XmlException) { }
    }

    private static void TestNativeSpeakerMatrixHttpPort80()
    {
        var handler = new FakeMatrixHandler();
        using (var transport = new DenonHttpTransport(handler, TimeSpan.FromSeconds(2)))
        using (var receiver = new DenonAvrClient("receiver.example", transport))
        {
            receiver.InitializeAsync().GetAwaiter().GetResult();
            Check(receiver.ReceiverProfileId == "legacy-goform",
                "The native speaker matrix should work with the generic legacy profile.");
            var result = receiver.DiscoverSpeakerMatrixAsync().GetAwaiter().GetResult();
            Check(result.HttpPort == 80 && result.Status == "Received" &&
                result.Slots.Count == 2 && result.Slots[0].Label == "FL",
                "AppCommand0300 on HTTP 80 should expose the native matrix without a speaker profile.");
            Check(handler.Requests.Any(req => req.RequestUri!.AbsolutePath.EndsWith("AppCommand0300.xml")),
                "The actual protocol API must be queried.");
            Check(handler.Requests.All(req => req.Method == HttpMethod.Get ||
                (req.Method == HttpMethod.Post &&
                req.RequestUri!.AbsolutePath.Contains("AppCommand"))),
                "Only documented read-only AppCommand POSTs may be sent.");
        }
    }

    private sealed class FakeMatrixHandler : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var xml = path == "/goform/Deviceinfo.xml"
                ? "<Device_Info><ModelName>*AVR-X4100W</ModelName>" +
                  "<CommApiVers>0300</CommApiVers><DeviceZones>3</DeviceZones></Device_Info>"
                : path == "/goform/AppCommand0300.xml"
                    ? "<rx><cmd><name>GetActiveSpeaker</name><list>" +
                      "<param name='activespa1' control='2'>FL</param>" +
                      "<param name='activespb1' control='1'>FR</param>" +
                      "</list></cmd></rx>"
                    : "<rx><cmd><status>ON</status></cmd></rx>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            });
        }
    }

    private sealed class FakeLegacyDiscoveryHandler : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var content = request.RequestUri!.AbsolutePath == "/"
                ? "<html><a href='/setup/speakers.html'>Speakers</a></html>"
                : request.RequestUri.AbsolutePath == "/goform/formMainZone_MainZoneXml.xml"
                    ? "<item><Speaker><Levels>exists</Levels></Speaker></item>"
                    : "<rx><cmd><status>ON</status></cmd></rx>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class FakeAppCommandHandler : HttpMessageHandler
    {
        private readonly bool _supportsAppCommand;
        private readonly int? _onlyPort;
        internal FakeAppCommandHandler(bool supportsAppCommand, int? onlyPort = null)
        {
            _supportsAppCommand = supportsAppCommand;
            _onlyPort = onlyPort;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var xml = request.RequestUri!.AbsolutePath switch
            {
                "/goform/Deviceinfo.xml" =>
                    "<Device_Info><ModelName>*AVR-X4100W</ModelName><ManualModelName>*AVR-X4100W</ManualModelName>" +
                    "<CommApiVers>0300</CommApiVers><DeviceZones>3</DeviceZones></Device_Info>",
                "/goform/AppCommand.xml" => (_supportsAppCommand &&
                    (!_onlyPort.HasValue || request.RequestUri.Port == _onlyPort.Value))
                    ? "<rx><cmd id='1'><zone1>ON</zone1></cmd></rx>"
                    : "<rx><error>unsupported</error></rx>",
                _ => "<rx/>"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            });
        }
    }

    private const string CapabilityFixture =
        "<Device_Info><ModelName>*AVR-X4100W</ModelName><MacAddress>PRIVATE</MacAddress>" +
        "<DeviceCapabilities><Setup><ChannelLevel><ChLists>" +
        "<Ch><Name>FL</Name><DispName>Front L</DispName><MinRange>0</MinRange>" +
        "<MaxRange>48</MaxRange><DefaultValue>24</DefaultValue><Step>0.5</Step></Ch>" +
        "<Ch><Name>TML</Name><DispName>Top Middle L</DispName><MinRange>0</MinRange>" +
        "<MaxRange>48</MaxRange><DefaultValue>24</DefaultValue><Step>0.5</Step></Ch>" +
        "<Ch><Name>ZRL</Name><DispName>Reest</DispName><MinRange>24</MinRange>" +
        "<MaxRange>24</MaxRange><DefaultValue>24</DefaultValue><Step>24</Step></Ch>" +
        "</ChLists><Functions><GetChLevel>1</GetChLevel><SetChLevel>1</SetChLevel></Functions>" +
        "</ChannelLevel><SoundMode><Commands><GetSoundModeList ver='1'>1</GetSoundModeList>" +
        "<SetSoundModeList ver='1'>0</SetSoundModeList></Commands>" +
        "<Genre><List><Value><DispName>MOVIE</DispName><CmdNo>MUSIC</CmdNo>" +
        "</Value></List></Genre></SoundMode></Setup></DeviceCapabilities>" +
        "<DeviceZoneCapabilities><Zone><No>0</No></Zone>" +
        "<Volume><MaxValue>98.0</MaxValue><StepValue>0.5</StepValue></Volume>" +
        "<Operation><QuickSelect><MaxQuickSelect>4</MaxQuickSelect></QuickSelect>" +
        "<TunerOperation><MaxPresets>56</MaxPresets></TunerOperation></Operation>" +
        "<SurroundMode><List><Mode><GroupName>Direct/Stereo</GroupName><Name>Stereo</Name>" +
        "</Mode><Mode><GroupName>DSP Simulation</GroupName><Name>Rock Arena</Name>" +
        "</Mode></List></SurroundMode></DeviceZoneCapabilities></Device_Info>";

    private static void TestDynamicDeviceCapabilities()
    {
        var catalog = DenonDeviceCapabilityReader.Parse(CapabilityFixture, 80);
        Check(catalog.IsCommandAdvertised("GetChLevel") &&
            catalog.IsCommandAdvertised("GetSoundModeList"),
            "Declared read functions must be discovered without a receiver profile.");
        Check(!catalog.IsCommandAdvertised("SetSoundModeList") &&
            catalog.Commands.Any(c => c.Name == "SetSoundModeList" && !c.Advertised),
            "Disabled functions must be retained, but not marked as supported.");
        Check(catalog.Channels.Count == 3 && catalog.Channels[1].NativeCode == "TML" &&
            catalog.Channels[0].Step == .5 && catalog.Channels[2].MayBePlaceholder,
            "Dynamic speaker definitions must retain raw ranges and odd placeholders.");
        Check(catalog.SoundModes.Count == 2 && catalog.SoundModes[1].Name == "Rock Arena" &&
            catalog.SoundModes.All(m => m.Name != "MUSIC"),
            "Grouped Main Zone catalog must not be confused with malformed Genre list.");
        Check(catalog.MaximumVolume == 98 && catalog.VolumeStep == .5 &&
              catalog.QuickSelectCount == 4 && catalog.TunerPresetCount == 56,
            "Device-reported limits must be preferred over hardcoded model assumptions.");
        Check(!catalog.ToString()!.Contains("PRIVATE") &&
            !string.Join(";", catalog.Commands.Select(c => c.Context)).Contains("PRIVATE"),
            "Capability objects and API output must not copy device identity fields.");
        try
        {
            DenonDeviceCapabilityReader.Parse(
                "<!DOCTYPE a [<!ENTITY leak SYSTEM 'file:///C:/secret.txt'>]>" +
                "<Device_Info>&leak;</Device_Info>", 80);
            throw new Exception("External entities must be prohibited.");
        }
        catch (System.Xml.XmlException) { }
    }

    private static void TestReadCommandClassification()
    {
        const string endpoint = "/goform/AppCommand0300.xml";
        var received = DenonReadCommandInspector.Inspect("GetChLevel", endpoint, 80,
            "<rx><cmd><name>GetChLevel</name><list>" +
            "<param name='chfl'>24</param></list></cmd></rx>");
        Check(received.Status == "Received" &&
            received.ResponseFields.Any(x => x.Contains("chfl")) &&
            !received.ResponseFields.Any(x => x.Contains("24")),
            "Probe must preserve XML parameter identity but exclude raw response values.");
        Check(DenonReadCommandInspector.Inspect("GetChLevel", endpoint, 80,
            "<rx><cmd><name>GetChLevel</name></cmd></rx>").Status == "Empty",
            "Echo-only command responses must not count as verified results.");
        Check(DenonReadCommandInspector.Inspect("GetSoundModeList", endpoint, 80,
            "<rx><error>unsupported</error></rx>").Status == "Rejected",
            "Explicit error replies must not count as verified data.");
    }

    private static void TestReadOnlyCapabilityHttpProbes()
    {
        var handler = new FakeCapabilityHandler();
        using (var transport = new DenonHttpTransport(handler, TimeSpan.FromSeconds(3)))
        using (var receiver = new DenonAvrClient("receiver.example", transport))
        {
            receiver.InitializeAsync().GetAwaiter().GetResult();
            Check(receiver.DeviceCapabilities != null &&
                  receiver.DeviceCapabilities.Channels.Count == 3,
                  "Initialize must populate an advertised catalog from Deviceinfo response.");
            var results = receiver.ProbeAdvertisedReadCommandsAsync().GetAwaiter().GetResult();
            Check(results.Count == 4 &&
                results.Count(p => p.Status == "Received") == 1 &&
                results.Any(p => p.Command == "GetSoundModeList" && p.Status == "Rejected"),
                "Only independently confirmed reply shapes may report Received.");
            Check(handler.Bodies.Count == 4 &&
                handler.Bodies.All(body => body.Contains("GetChLevel") ||
                    body.Contains("GetSoundModeList")) &&
                handler.Bodies.All(body => !body.Contains("<name>Set") &&
                    !body.Contains(">Set")),
                "Probe must never send a write command.");
            var refreshed = receiver.RefreshDeviceCapabilitiesAsync().GetAwaiter().GetResult();
            Check(refreshed.QuickSelectCount == 4 && handler.DeviceGets >= 2,
                "Explicit refresh must refetch Deviceinfo without changing receiver state.");
        }
    }

    private sealed class FakeCapabilityHandler : HttpMessageHandler
    {
        internal int DeviceGets { get; private set; }
        internal List<string> Bodies { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string data;
            if (path == "/goform/Deviceinfo.xml")
            {
                DeviceGets++;
                data = CapabilityFixture;
            }
            else if (path == "/goform/AppCommand.xml" ||
                     path == "/goform/AppCommand0300.xml")
            {
                var body = await request.Content!.ReadAsStringAsync();
                if (body.Contains("GetAllZonePowerStatus"))
                    data = "<rx><cmd><zone1>ON</zone1></cmd></rx>";
                else
                {
                    Bodies.Add(body);
                    if (body.Contains("GetChLevel") && path.EndsWith("0300.xml"))
                        data = "<rx><cmd><name>GetChLevel</name><list>" +
                            "<param name='chfl'>24</param></list></cmd></rx>";
                    else if (body.Contains("GetSoundModeList"))
                        data = "<rx><error>unsupported</error></rx>";
                    else data = "<rx><cmd><name>GetChLevel</name></cmd></rx>";
                }
            }
            else data = "<rx/>";
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(data) };
        }
    }

    private sealed class FakeSpeakerHandler : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var query = request.RequestUri!.Query;
            var type = query.Split('&').First(part => part.StartsWith("?type=") || part.StartsWith("type="))
                .Split('=')[1];
            if (type == "3")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var xml = type switch
            {
                "5" => "<Levels><List><Speaker index='0'>0</Speaker></List></Levels>",
                "4" => "<Distances><Unit>1</Unit><Step>1</Step><List><Speaker index='0'>300</Speaker></List></Distances>",
                "1" => "<listSetupMenu><Levels>3</Levels></listSetupMenu>",
                "15" => "<SpeakerLayout><List><Item index='1' display='3'/></List></SpeakerLayout>",
                _ => "<Unexpected/>"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            });
        }
    }

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
            throw new Exception("Expected InvalidDataException.");
        }
        catch (InvalidDataException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
