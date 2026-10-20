# DenonMarantzAvrNet

`DenonMarantzAvrNet` is an asynchronous C# library for controlling compatible Denon and Marantz AV receivers through their local HTTP/XML and Telnet interfaces. The repository also contains a WPF tester application for trying out the library against a real receiver.

The library was developed primarily with a **Denon AVC-X6800H** and its `0301` communication API on HTTP port `8080`. The **AVC-X6700H** is supported through its own receiver profile. Older devices with their status interface on port `80` are supported for the basic functions.

## Features

- Automatic `Deviceinfo.xml` discovery on HTTP port `80` or `8080`; **independent** read-only `AppCommand.xml`/`AppCommand0300.xml` capability probes on both candidate ports
- Reads the model, API version, number of zones, MAC address, and device category
- Receiver profiles selected from the reported model (`avc-x6800h`, `avc-x6700h`, `legacy-goform`, `unknown`), with a manual override for similar models (compatibility mode)
- Main Zone status:
  - Power
  - Selected input
  - Master volume
  - Mute
  - Available inputs
- Zone 2 and Zone 3 status (power, input, volume, mute)
- Cached input list with explicit refresh
- Automatic compatibility fallback for incomplete bundled AppCommand responses
- Extended audio information on compatible devices:
  - Audio input mode
  - Audio output
  - Detected audio format
  - Active sound mode
  - Sample rate
  - Active speaker channels
- Unified Main Zone control through HTTP or Telnet with automatic selection:
  - Turn on and off
  - Increase/decrease volume or set it on Denon's absolute `0..98` scale; explicit dB setters are also available
  - Enable and disable mute
  - Select an input by its visible Denon name
- Zone 2 and Zone 3 volume (up/down/set) and mute through the same unified API
- Shared persistent Telnet command/event session, serial command correlation, prefix-checked replies, and automatic reconnect on failure
- Telnet client (`DenonTelnetClient`) for everything that is Telnet-only or Telnet-specific:
  - Queries: power, volume, mute, input, surround mode, digital input mode, Zone 2, Zone 3
  - Zone 2/3 power and input selection
  - Surround mode: Auto, Stereo, Dolby Surround, DTS Neural:X, Multi Ch Stereo, or Pure Direct
  - Digital input decoder: Auto, PCM, or DTS
  - Switch speaker preset 1 or 2 (`SPPR`)
  - Raw Telnet commands
- Read and control the temporary channel levels of the current surround mode through Telnet (`CV`):
  - Read individual channels or all configured channels (`CV?` / `CVEND`)
  - Set levels from -12.0 to +12.0 dB or change them in steps
  - Set subwoofer channels to OFF and reset all channel levels to Denon factory values
- Read and set the actual levels of the active speaker preset through the receiver-specific speaker web interface (X6800H: HTTP/11080; X6700H: HTTPS/10443)
- Read-only native speaker index discovery (`DiscoverSpeakerIndicesAsync`) from the live speaker AJAX levels, distances, menu, configuration and optional layout pages; raw IDs and separate index namespaces are preserved, with profile hints clearly identified
- Expose the number of speaker presets known for the detected receiver profile
- Capture and restore persistent speaker-preset level snapshots
- Read the active speaker preset and switch it through the speaker web interface; the call returns once the receiver confirms the new preset
- Combine persistent speaker levels and distances into configured physical speakers with `GetConfiguredSpeakersAsync()`
- Read and set persistent speaker distances; public distance values are normalized to meters
- Persistent status monitoring for headless operation (`DenonReceiverMonitor`):
  - Telnet events are received immediately
  - `OPINFASP` speaker matrices are decoded and duplicate telemetry is filtered
  - Automatic reconnection after a Telnet interruption
  - HTTP status query every 15 seconds as a fallback
- Optional file logger for all HTTP and Telnet traffic (`ReceiverLogger`)
- Send arbitrary raw Denon HTTP command paths
- `CancellationToken` support

## Tester application

`DenonMarantzAvrNet.TesterGui` is a WPF application (English and German) for testing the library against your receiver:

- Connects to a host or IP address and shows the detected model and receiver profile
- Receiver profile dialog defaults to **Auto** (including `legacy-goform` for older models). A manual choice of another model is an optional, explicitly displayed **compatibility mode**; it is never required merely to connect.
- Command list grouped by Status, Main Zone, Zones 2/3, Speakers (HTTP) and Audio / Levels (Telnet). Commands can be sent over Auto, HTTP, or Telnet where the library supports it.
- Separate output panes for the result, the HTTP traffic and the Telnet traffic, read from the library log
- A **Discover native speaker indices (read-only)** command for comparing native level/distance IDs and optional menu/layout metadata across receiver models
- **Check receiver compatibility...** runs a set of **read-only** operations against your receiver, shows each result as green (ok), yellow (warning) or red (error), and prepares a JSON report. You can choose a regular or an anonymized report. The anonymized report leaves out the MAC address and host. The report is only shown and copied to the clipboard. **Nothing is sent automatically**: you paste it into an e-mail yourself.

Some commands in the tester change receiver settings (power, volume, input, levels, distances, presets). The compatibility check does not.

For details of this change, the read-only checks and their limitations, see [HTTP discovery and Telnet session](docs/TRANSPORT-DISCOVERY.md).

## Requirements

- Windows with the .NET Framework 4.8 runtime
- Visual Studio or the .NET SDK with the .NET Framework 4.8 targeting pack to build
- Receiver and application on the same reachable network
- Network control enabled on the receiver

The library targets `net48` and uses C# `latest`. It depends on `Microsoft.Bcl.AsyncInterfaces` and `System.Memory`, which are restored as NuGet packages.

## Project structure

| Project | Contents |
| --- | --- |
| `DenonMarantzAvrNet` | Reusable class library |
| `DenonMarantzAvrNet.TesterGui` | WPF tester and receiver compatibility check |
| `DenonMarantzAvrNet.Tests` | Standalone, dependency-light net48 offline discovery regression checks |

## Referencing the library

Until a NuGet package is published, add the library as a project reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\DenonMarantzAvrNet\DenonMarantzAvrNet.csproj" />
</ItemGroup>
```

## Quick start

```csharp
using DenonMarantzAvrNet;
using DenonMarantzAvrNet.Models;

using var receiver = new DenonAvrClient("10.37.0.190");

var device = await receiver.InitializeAsync();
Console.WriteLine($"{device.ModelName}, API {device.CommunicationApiVersion}");
Console.WriteLine($"HTTP port: {receiver.HttpPort}");
Console.WriteLine($"Profile: {receiver.ReceiverProfileId}");

var state = await receiver.UpdateAsync();
Console.WriteLine($"Power: {state.Power}");
Console.WriteLine($"Input: {state.Input}");
Console.WriteLine($"Volume: {state.VolumeDb} dB");
Console.WriteLine($"Audio format: {state.Audio?.AudioFormat ?? "unknown"}");
Console.WriteLine($"Sound mode: {state.Audio?.SoundMode ?? "unknown"}");
Console.WriteLine($"Active speakers: {string.Join(", ", state.Audio?.ActiveSpeakers ?? [])}");
Console.WriteLine($"Active channels: {state.Audio?.ActiveSpeakerChannels ?? SpeakerChannel.None}");
```

`InitializeAsync()` must complete successfully once before status or control commands can be used. The HTTP request timeout of `DenonAvrClient` defaults to 30 seconds and can be passed to the constructor.

## Controlling the receiver

```csharp
await receiver.PowerOnAsync();
await receiver.VolumeUpAsync();
await receiver.VolumeDownAsync();
await receiver.SetVolumeAsync(40.0);       // Denon absolute scale
await receiver.SetVolumeDbAsync(-40.0);    // explicit dB alternative
await receiver.SetMuteAsync(true);
await receiver.SetMuteAsync(false);
await receiver.SetInputAsync("Media Player");

await receiver.SetZone2VolumeDbAsync(-45.0);
await receiver.SetZone2MuteAsync(true);
```

## Unified HTTP/Telnet control

The regular Main Zone and Zone 2/3 volume and mute methods (`PowerOnAsync`, `SetVolumeAsync`, `SetMuteAsync`, `SetInputAsync`, `Zone2VolumeUpAsync`, and so on) form a unified API. There are no separate HTTP and Telnet method names. Choose the transport with `DenonControlProtocol`:

```csharp
// Default: Auto. After InitializeAsync, HTTP is used; without HTTP initialization, Telnet is used.
await receiver.SetVolumeAsync(55.0); // 55 on the Denon scale = -25 dB

// Explicitly send one command over Telnet:
await receiver.SetInputAsync("CBL/SAT", DenonControlProtocol.Telnet);

// Prefer Telnet permanently in an application (for example, AVR-X4100W):
receiver.PreferredControlProtocol = DenonControlProtocol.Telnet;
```

With `Auto`, HTTP is tried after successful HTTP initialization. If the HTTP control command produces a network/HTTP error such as `403`, the library tries Telnet. When `Http` or `Telnet` is selected explicitly, there is no fallback, which enables predictable behavior and easier diagnostics. Features that are implemented for only one transport throw a `NotSupportedException` when you request the other one.

## Telnet client

`DenonTelnetClient` talks to the receiver on port `23`. Each command opens its own short-lived connection and returns the first response line. The default timeout is 3 seconds.

```csharp
var telnet = new DenonTelnetClient("10.37.0.190");

Console.WriteLine(await telnet.QueryPowerAsync());       // PW?
Console.WriteLine(await telnet.QueryVolumeAsync());      // MV?
Console.WriteLine(await telnet.QueryZone2Async());       // Z2?

await telnet.SetZone2PowerAsync(true);
await telnet.SetZone2InputAsync("Media Player");
await telnet.SetSurroundModeAsync("Dolby Surround");
await telnet.SetDigitalInputModeAsync("Auto");
await telnet.SelectSpeakerPresetAsync(1);                // Telnet preset 1 or 2

var response = await telnet.SendCommandAsync("MV?");    // raw command, no line breaks allowed
```

Temporary channel levels of the current surround mode (Telnet `CV` commands) are also available through `DenonAvrClient`:

```csharp
var levels = await receiver.GetSpeakerLevelsAsync(DenonControlProtocol.Telnet);
await receiver.SetSpeakerLevelAsync(DenonSpeakerLevelChannel.FrontLeft, -1.5, DenonControlProtocol.Telnet);
await receiver.SetSpeakerLevelOffAsync(DenonSpeakerLevelChannel.Subwoofer2); // subwoofer channels only
```

### Receiver profile and compatibility mode

During `InitializeAsync()` the library selects a receiver profile from the reported model name (`X6800` or `X6700`). Receivers on port `80` that are not recognized get the `legacy-goform` profile; all others get `unknown`. These two profiles have no speaker web interface support.

If your receiver is a similar model that is not recognized, you can test it with the profile of a supported model:

```csharp
await receiver.InitializeAsync();

if (receiver.ReceiverProfileId is "unknown" or "legacy-goform")
{
    receiver.SelectReceiverProfile("avc-x6700h"); // or "avc-x6800h"
}
```

The override stays active until the next successful `InitializeAsync()`. An unsupported profile ID throws an `ArgumentException`.

### Web-interface speaker setup

The values under **Setup → Speakers → Levels** are not the same as Telnet `CV` values. The X6800H and X6700H profiles use Denon's AJAX speaker-setup interface, but with model-specific transport details: the X6800H uses HTTP on port `11080`, while the X6700H uses HTTPS on port `10443`. The X6700H's local device certificate is accepted only for this explicitly profiled speaker-setup connection.

```csharp
Console.WriteLine($"Speaker presets: {receiver.SpeakerPresetCount}");

var levels = await receiver.GetSpeakerPresetLevelsAsync();
await receiver.SetSpeakerPresetLevelAsync(speakerIndex: 2, decibels: -3.5);

var snapshot = await receiver.CreateSpeakerLevelSnapshotAsync();
// ... temporary persistent-level changes ...
await receiver.RestoreSpeakerLevelSnapshotAsync(snapshot);
```

`SpeakerPresetCount` comes from the selected receiver profile. The X6800H and X6700H profiles currently report two presets; legacy and unknown profiles report zero. `SpeakerIndex` is the web-interface index provided by the receiver. Preset levels must be within ±12.0 dB.

The same profile-specific speaker setup interface can read and write persistent speaker distances on both the X6800H and X6700H:

```csharp
var distances = await receiver.GetSpeakerDistancesAsync();
Console.WriteLine($"Receiver unit: {distances.ReceiverUnit}, step: {distances.Step:0.###} m");

foreach (var speaker in distances.Speakers)
    Console.WriteLine($"{speaker.Channel}: {speaker.Meters:0.###} m");

await receiver.SetSpeakerDistanceAsync(speakerIndex: 0, meters: 3.25);
```

Public distance values are always expressed in meters. The captured X6700H interface reports raw distance values as meters × 100 even when the receiver display unit is configurable. The test tone is not implemented as a public feature; the X6800H profile retains its confirmed stop command after a preset switch, while the X6700H preset path does not send an unverified test-tone command.

Configured speakers are intentionally different from currently active audio channels. `GetConfiguredSpeakersAsync()` joins the persistent Levels and Distances pages by `SpeakerChannel` and returns physical speakers configured in the active preset. `state.Audio.ActiveSpeakerChannels`, by contrast, describes channels currently active in the audio signal and must not be used as a configuration filter.

```csharp
var configured = await receiver.GetConfiguredSpeakersAsync();

foreach (var speaker in configured)
    Console.WriteLine($"{speaker.Channel}: {speaker.LevelDb:0.0} dB, {speaker.DistanceMeters:0.00} m");

var preset = await receiver.GetActiveSpeakerPresetAsync();
await receiver.SelectSpeakerPresetAsync(preset == 1 ? 2 : 1);
```

`SelectSpeakerPresetAsync()` waits until the receiver confirms the new preset. A switch can take several seconds (the default confirmation timeout is 30 seconds, configurable with the `confirmationTimeout` parameter). Calls must not overlap with other calls on the same client.

### Feature and device capabilities

`AvrFeature` describes a library feature, not hardware. Use `GetSupportedProtocols()` to find which transport is implemented for a feature:

```csharp
var transports = receiver.GetSupportedProtocols(AvrFeature.MainZoneVolume);
// Contains Http and Telnet.
```

`ReceiverCapabilities`, on the other hand, describes the detected AVR. After `InitializeAsync()`, HTTP, AppCommand, the zone count, and Zone 2/3 are known. Support for AppCommand0300 is detected at the first `UpdateAsync()`. Telnet is intentionally not probed automatically; `PW?` can check it without changing state:

```csharp
await receiver.InitializeAsync();

var capabilities = await receiver.ProbeReceiverCapabilitiesAsync();
Console.WriteLine(capabilities.SupportsTelnet);          // true or false
Console.WriteLine(capabilities.SupportsAppCommand0300);  // true, false, or null before UpdateAsync
Console.WriteLine(receiver.IsFeatureAvailable(AvrFeature.Zone3Control));
Console.WriteLine(receiver.IsFeatureAvailable(AvrFeature.SpeakerPresetSelection));
```

A control command does not update `receiver.State` in advance. Query the receiver again to obtain a device-confirmed state:

```csharp
await receiver.SetInputAsync("CBL/SAT");
await Task.Delay(350);
var confirmedState = await receiver.UpdateAsync();
```

`AvailableInputs` is loaded on the first status request and cached afterwards. Explicitly reload it after changing the receiver's input configuration:

```csharp
var currentInputs = await receiver.RefreshInputsAsync();
```

## Monitoring

`DenonReceiverMonitor` keeps a status snapshot up to date. It listens to unsolicited Telnet events and refreshes the HTTP status after each event (with a short coalescing window). It also polls the HTTP status as a fallback, every 15 seconds by default.

```csharp
await using var monitor = new DenonReceiverMonitor("10.37.0.190");

monitor.StateRefreshed += state => Console.WriteLine($"{state.Power}, {state.VolumeDb} dB");
monitor.TelnetEventReceived += telnetEvent => Console.WriteLine(telnetEvent.Message);
monitor.Error += exception => Console.Error.WriteLine(exception.Message);

await monitor.StartAsync();
// monitor.CurrentState always holds the last snapshot.
```

Handlers run on background threads and should not perform long-running work. `DenonTelnetEventListener` can also be used on its own; it reconnects automatically after a connection loss.

## Logging

`ReceiverLogger` writes all HTTP requests and responses and all Telnet commands and responses to a file. It is **disabled by default** and does nothing until you enable it.

```csharp
using DenonMarantzAvrNet.Logger;

ReceiverLogger.FilePath = Path.Combine(Path.GetTempPath(), "denon.log");
ReceiverLogger.Clear();
ReceiverLogger.Enabled = true;
ReceiverLogger.TelnetLoggingEnabled = true; // default
```

The default path is `c:\temp\denon.log`. Log lines contain the host and complete response bodies, which can include device identifiers such as the MAC address. Review a log before sharing it. The tester application uses this log to fill its HTTP and Telnet output panes.

## Input names

Denon sometimes uses protocol names that differ from the user interface. `SetInputAsync()` maps the most important display names automatically:

| Display name | Denon protocol name |
| --- | --- |
| `CBL/SAT` | `SAT/CBL` |
| `Media Player` | `MPLAY` |
| `TV AUDIO` | `TV` |
| `Blu-ray` | `BD` |
| `Bluetooth` | `BT` |
| `NETWORK` | `NET` |
| `iPod/USB` | `USB/IPOD` |
| `AUX` | `AUX1` |
| `Tuner` or `FM` | `TUNER` |
| `SpotifyConnect` | `Spotify Connect` |

Mapping is case-insensitive. Known protocol names such as `MPLAY` or `SAT/CBL` can also be passed directly. The same mapping is used by the Telnet client.

## Status model

`UpdateAsync()` returns a `DenonReceiverState` and stores the same snapshot in `receiver.State`.

| Property | Meaning |
| --- | --- |
| `IsPoweredOn` | Simplified Boolean power status |
| `Power` | Unchanged receiver power text |
| `Input` | Current Denon input protocol name |
| `Volume` | Master volume on Denon's absolute `0..98` scale or `null` |
| `VolumeDb` | The same master volume in relative dB or `null` |
| `IsMuted` | Mute status or `null` |
| `AvailableInputs` | Standard inputs that are not disabled |
| `Audio` | Optional extended audio information |
| `Zone2`, `Zone3` | Optional status snapshots for additional zones |

`DenonAudioInfo` contains:

| Property | Meaning |
| --- | --- |
| `InputMode` | For example, `HDMI` |
| `Output` | For example, `Speaker` |
| `AudioFormat` | Detected input signal, e.g. Dolby Digital Plus |
| `SoundMode` | Active playback/upmix mode |
| `SampleRate` | For example, `48 kHz` |
| `ActiveSpeakers` | Active Denon channel abbreviations such as `FL`, `C`, `SW`, or `TFL` |
| `ActiveSpeakerChannels` | The same channels as a combinable `SpeakerChannel` flags enum |

`Audio` can be `null` on older or incompatible receivers, and individual values can be absent. A receiver-reported `Unknown` is deliberately not reinterpreted.

## Errors and cancellation

```csharp
using DenonMarantzAvrNet.Exceptions;

try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await receiver.InitializeAsync(timeout.Token);
    var state = await receiver.UpdateAsync(timeout.Token);
}
catch (DenonConnectionException exception)
{
    Console.Error.WriteLine($"Receiver is unreachable: {exception.Message}");
}
catch (DenonProtocolException exception)
{
    Console.Error.WriteLine($"Unexpected receiver response: {exception.Message}");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation cancelled or timed out.");
}
```

| Exception | Meaning |
| --- | --- |
| `DenonConnectionException` | Valid device information was not found on any supported port |
| `DenonProtocolException` | XML or expected Denon status values are invalid or missing, or the receiver did not confirm a speaker preset switch in time |
| `HttpRequestException` | HTTP or network error during a request |
| `IOException` / `SocketException` | Telnet connection problem, or the receiver sent no response |
| `OperationCanceledException` | The operation was cancelled, or a Telnet command exceeded its timeout |
| `NotSupportedException` | The feature is not implemented for the selected transport or receiver profile |
| `InvalidOperationException` | The client has not been initialized yet |
| `ArgumentException` | Invalid argument, for example an empty input name, a line break in a command, or an unknown profile ID |
| `ArgumentOutOfRangeException` | Absolute volume outside `0.0..98.0`, dB volume outside `-80.0..+18.0 dB`, channel or preset level outside ±12.0 dB, or another numeric argument outside its supported range |

## Raw commands

Receiver features that do not yet have a dedicated method can be addressed with a complete Denon HTTP command path:

```csharp
await receiver.SendCommandAsync(
    "/goform/formiPhoneAppDirect.xml?VSMONI2");
```

The path must begin with `/`. The calling application is responsible for ensuring that the specific receiver supports the command. Raw Telnet commands are sent with `DenonTelnetClient.SendCommandAsync()`.

## Building and running

```powershell
dotnet build
```

Start `DenonMarantzAvrNet.TesterGui` from Visual Studio or run the built executable from its output folder.

## Technical notes

- Port `8080` uses `AppCommand.xml` with `cmd id="1"` for base status. Four status commands are initially bundled in one request.
- If a receiver does not return a fully usable response, the client repeats the query with individual requests and remembers this variant until the next initialization.
- Audio format and active speakers are queried through `AppCommand0300.xml` with `cmd id="3"`.
- Detail queries are sequential; parallel access is avoided.
- The input list is loaded separately and cached in the client.
- The AVC-X6800H requires a CRLF after the XML declaration. Without it, the receiver returns HTTP 200 with an empty `<rx>` element.
- Telnet commands use one short-lived TCP connection each (port `23`); multi-line responses such as `CV?` are read until their terminating `CVEND` line.
- Do not use `DenonAvrClient` concurrently through multiple parallel `UpdateAsync()` calls.

Further details are available in the [API documentation](docs/API.md), [protocol notes](docs/PROTOCOL.md), and [device-testing guide](docs/DEVICE-TESTING.md).

## Current limitations

- The current focus is the Main Zone.
- Renamed inputs are not yet mapped separately to their custom display names.
- Events are received through a permanently open Telnet connection; custom event handlers should not perform long-running work.
- Receiver-specific web features use an internal profile selected during `InitializeAsync()`. The selected profile is exposed as `ReceiverProfileId` for diagnostics, and can be replaced with `SelectReceiverProfile()` for similar models.
- Speaker-preset levels, speaker distances, and HTTP speaker-preset selection are implemented for both the AVC-X6800H and AVC-X6700H through receiver-specific AJAX profiles. The X6800H uses HTTP/11080; the X6700H uses HTTPS/10443 and an explicitly scoped local-device certificate exception. Legacy and unknown profiles currently report zero known presets and no web speaker-setup support.
- Manual profile selection only offers the X6800H and X6700H profiles.

## Help the project

Run **Check receiver compatibility...** in the tester application with your receiver, check the generated report, and send it by e-mail to the project. The check only reads values. Nothing is sent automatically and nothing is changed on your receiver.

## Reference and license

The implementation was inspired by [`ol-iver/denonavr`](https://github.com/ol-iver/denonavr). The original reference version is based on commit `98566b286efab12496ef623eda498db8bcb5ea09` (`1.4.0-dev`).

License and provenance notices are in [LICENSE.txt](LICENSE.txt) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).