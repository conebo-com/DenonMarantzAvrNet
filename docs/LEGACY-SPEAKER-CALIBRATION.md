# Legacy speaker setup: persistent levels and distances

## Verified AVR-X4100W behavior (2026-10-20)

The AVR-X4100W serves *actual persistent* speaker calibration through two ordinary HTTP GET pages. These are NOT the per-input OPTION Channel Level Adjust (`GetChLevel`) and NOT Telnet `CV?` status:

- `GET /SETUP/SPEAKERS/LEVELS/d_speakersetup.asp`
- `GET /SETUP/SPEAKERS/DISTANCES/d_speakersetup.asp`

The outer `f_speakersetup.asp` pages are only HTML framesets; fetching those does not reveal channel values. Both content pages contain the values in HTML `<input>` attributes on the server response, without requiring JavaScript execution.

### Protocol fields

- **Levels:** hidden `name='textCVFL' value='-6.0'`, etc., in `Speakers/Levels`. Channel suffixes (`FL`, `C`, `FR`, `SL`, `SR`, `SB`, `TML`, `TMR`, `SW`) are derived from the page, not hardcoded by model. Values are signed dB; the observed page uses -12..+12 dB in 0.5-dB steps.
- **Distances:** `name='textDelayTimeFl' value='1.00'`, etc., in `Speakers/Distances`. The selected `radioDelayTimeMode` (`M` or `F`) determines whether the raw numbers are meters or feet. Returned public `Meters` is normalized; the raw displayed value and unit are also preserved.
- The pages contain a `method='POST' action='s_speakersetup.asp'` form and a **Set** button. Those are **not invoked** by this read-only implementation. Never infer a safe write format from the HTML alone.

The page's input order is not guaranteed to be the same as AppCommand active-speaker matrix order, and no AJAX integer speaker index is invented for these fields. Unknown future native channel codes are retained.

### Public C# API

```csharp
await receiver.InitializeAsync();
var calibration = await receiver.GetLegacySpeakerCalibrationAsync();
foreach (var channel in calibration.Levels)
    Console.WriteLine($"{channel.NativeChannelCode}: {channel.Decibels:0.0} dB");
foreach (var channel in calibration.Distances)
    Console.WriteLine($"{channel.NativeChannelCode}: {channel.Meters:0.00} m");
```

This method uses the HTTP port detected at initialization and validates the pages. It is deliberately independent of model/profile lookup and the modern AJAX preset API. The WPF support report adds **Legacy calibration levels and distances (read only)** for automatically detected legacy devices.

### Limitations and testing

- Read-only. The form POST semantics must be validated separately before implementing any writes.
- An HTML page may be unavailable in Pure Direct, under setup lock, on different generations or firmware. The parser rejects missing, inconsistent, duplicated, out-of-range, or unit-ambiguous values instead of silently returning fabricated data.
- Device-specific UI may display last-saved distances (e.g., all 1.00 m), which this code faithfully reports; it does not claim these are measured physical distances.
- Existing modern X6700H/X6800H AJAX speaker-presets, snapshot, distance read/write and device profiles remain unchanged. A common cross-generation read API can be layered over the two representations later without confusing speaker presets with legacy single setup.
- A Windows .NET Framework 4.8 build and on-device read-only re-test are required. Offline tests use synthetic fixtures, including signed level values, channel names, feet/meter units, malformed pages and HTTP GET-only checks.
