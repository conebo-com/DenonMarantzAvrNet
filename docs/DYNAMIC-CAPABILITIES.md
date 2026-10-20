# Dynamic DeviceInfo capabilities / AVR-X4100W (read-only)

## Scope and evidence (2026-10-20)

The X4100W was tested with the main zone powered on, with HTTP `Deviceinfo.xml`,
`AppCommand.xml` and `AppCommand0300.xml` on port 80. The receiver's full
`Deviceinfo.xml` advertises (among other things):

- `DeviceCapabilities/Setup/ChannelLevel/ChLists/Ch`: 29 native channel types,
  including `FL`, `C`, `SW`, `SW2`, `TML`, `TMR` and the unusual `ZRL` entry.
- `ChannelLevel/Functions/GetChLevel=1` and `SetChLevel=1` with a per-channel
  step of `0.5` and values typically ranging from 0 to 48.
- `Setup/SoundMode/Commands/GetSoundModeList=1`, `GetSoundMode=1`,
  `SetSoundMode=1` and `SetSoundModeList=1`.
- `DeviceZoneCapabilities` with zone number `0` describing thirteen grouped
  Main Zone sound modes.
- `Volume/MaxValue=98.0`, `Volume/StepValue=0.5`, four Quick Select slots and
  56 tuner presets.

**Three distinct concepts must remain distinct:**

1. `ChLists` describes **possible/addressable channel types**, not configured
   speakers. `ZRL` has a fixed range of 24..24 and may be a placeholder.
2. `GetActiveSpeaker` describes the current matrix of native slots and short
   speaker labels. In the X4100W test, 9 of 20 slots had labels, both when
   powered on and in standby. Raw `control` values must be preserved.
3. `CV?` returns **playback channel trims**. It consistently returned only
   `CVFL 50`, `CVFR 50`, `CVEND`; this does not limit the list of calibrated or
   available speaker types.

**Still unknown:** the older receiver's Audyssey setup/calibration HTTP API
for saved speaker levels and distances. `GetChLevel` is NOT automatically
considered the same thing as Audyssey calibration.

## Implementation

`DenonAvrClient.DeviceCapabilities` is initialized automatically from the same
`Deviceinfo.xml` fetched during `InitializeAsync()` (no extra HTTP request).
`RefreshDeviceCapabilitiesAsync()` explicitly reloads the catalog. Neither
method uses the profile-specific speaker AJAX table.

The model `DenonDeviceCapabilityDiscovery` exposes:

- `Commands`: enabled **and disabled** read/write command declarations,
  including context, source `Functions`/`Commands`, and optional version.
- `Channels`: all native codes, optional firmware labels and raw advertised
  min/max/default/step. `MayBePlaceholder` marks a fixed-range entry but does
  not delete it.
- `SoundModes`: grouped **static Main Zone catalog** from
  `DeviceZoneCapabilities` (zone `0`) / `SurroundMode/List`. These are not
  input-format-dependent availability lists and not the Movie/Music/Game/Pure
  button groups. The malformed/conflicting `SoundMode/Genre` block is **not**
  interpreted as an authoritative group mapping.
- `MaximumVolume`, `VolumeStep`, `QuickSelectCount`, `TunerPresetCount`.

`ProbeAdvertisedReadCommandsAsync()` performs up to **four read-only POSTs**,
for `GetChLevel` and `GetSoundModeList` against both `AppCommand.xml` (`id=1`)
and `AppCommand0300.xml` (`id=3` with empty list). It only attempts commands
advertised as enabled in the device catalog. This pair of request shapes is
experimental: firmware may require additional arguments or a different
request envelope. A `Rejected` or `Empty` reply therefore is **not proof of
missing capability**, and `Received` confirms only a response shape, not
correct semantic decoding.

The WPF compatibility report adds two read-only sections, collecting only
known standard codes, numeric boundaries, field paths and sanitized catalog
names. Receiver-specific XML values, MAC addresses, configured friendly names,
raw sound list contents and host addresses are omitted.

## Public example

```csharp
using (var receiver = new DenonAvrClient("receiver.local"))
{
    await receiver.InitializeAsync();
    var catalog = receiver.DeviceCapabilities;
    foreach (var channel in catalog.Channels)
        Console.WriteLine(channel.NativeCode + ": " + channel.Step);

    // Optional refetch, read-only:
    catalog = await receiver.RefreshDeviceCapabilitiesAsync();
    var probes = await receiver.ProbeAdvertisedReadCommandsAsync();
    foreach (var probe in probes)
        Console.WriteLine(probe.Command + " " + probe.Endpoint + " " + probe.Status);
}
```

## Follow-up hardware validation

Run an anonymized compatibility report while the X4100W is powered on.
Share the two new sections **Device capabilities (dynamic, read only)** and
**Advertised read commands (experimental)**. Pay attention to both endpoints
of `GetChLevel` and `GetSoundModeList`; `Empty`, `Rejected` or `Error` must not
be converted to `unsupported` automatically. If successful, subsequent work
will add verified semantic parsers for each response and integrate a dynamic
sound mode API.

All projects remain on .NET Framework 4.8 (`net48`). No speaker calibration,
input, volume or sound mode **write** operations were added or changed.
