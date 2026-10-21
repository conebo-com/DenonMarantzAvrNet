# AVC-X6700H: Cross-profile transport and calibration handling

## Findings from the two 2026-10-21 hardware reports

- Both reports identified **AVC-X6700H**, API **0301**, HTTP/AppCommand on **8080**, and three zones.
- `legacy-goform` was deliberately forced in the first report. Modern preset and setup operations were hidden, and the unified calibration request ended in HTTP **403 Forbidden** because it attempted the wrong legacy HTML setup pages.
- With the detected `avc-x6700h` profile, the modern AJAX interface returned speaker preset 1, 15 raw level entries (including the SW+SW2 group entry), 14 individual channel distances and a complete 14-channel unified calibration result after group filtering.
- `GetActiveSpeaker` returned labeled matrix positions in both profiles. A native matrix is not a list of currently sounding channels or persistent calibration indices.
- `DC?` produced no response before timeout; optional `OPSMLALL` ended with `IOException`. This does not disprove `MSMOVIE`/`MSMUSIC`/`MSGAME`/`MSDIRECT` commands, which are independently selectable and should be confirmed via `MS?`.

## Changes

### Physical transport resolution

`DenonAvrClient.GetSpeakerTransportProfile()` derives the speaker provider from the **actual device identity and HTTP port**, independent from the optional WPF compatibility-profile override. It resolves AVC-X6700H to the HTTPS:10443 speaker AJAX transport even if the UI profile shows `legacy-goform`. AVC-X6800H remains HTTP:11080 and legacy port-80 receivers continue to use legacy HTML.

For an **unknown** API-0301 model detected via HTTP:8080, `GetSpeakerCalibrationAsync()` performs bounded, read-only AJAX trials against known transport variants, validates that channel levels and distances were actually returned, then caches a confirmed provider. Failed trials are not evidence that the feature is unsupported. No writes are used for discovery.

This is not a universal guarantee for arbitrary new receiver protocols: unknown HTTPS ports and payload formats still require implementation when identified.

### Calibration results

`GetSpeakerCalibrationAsync()` excludes known multi-bit group control entries from the list of physical channels; the 15th X6700H preset level is still obtainable through `GetSpeakerPresetLevelsAsync()` for diagnostics. Unknown speaker indices are preserved as unclassified raw entries rather than assigned guessed channel names.

The existing safe/confirmed-write rules remain unchanged: no automatic writes in compatibility reports and actual writes must be verified by readback. For unknown models, a successful read-only probe must select a provider before a calibration write is attempted.

### Main Zone and sound modes

When no audio-output channels are reported (for example, a stereo/no-signal scenario), the compatibility report distinguishes the absence of active-channel telemetry from the receiver's reported speaker matrix. Labels from `GetActiveSpeaker` are shown as **native positions, not active outputs**.

`OPSMLALL` is optional. Generic family commands can be tried with the one-pass mode switching and restoration test, which verifies the actual mode via `MS?`. An optional `DC?` timeout is a **warning**, not a failure of Telnet control generally.

## Single follow-up hardware test

1. Build the full `net48` solution and connect AVC-X6700H with **automatic profile**.
2. Run the anonymized compatibility report. The unified calibration should report **14 physical channels** and no `[hidden]` combined subwoofer row; the native speaker matrix should show its own positions separately.
3. Optionally force `legacy-goform`, run the report again, and confirm that modern preset/level/distance endpoints continue working without HTTP 403 (compatibility override must no longer mask physical transport).
4. Run the existing **one-pass Movie/Music/Game/Direct/Pure Direct switch/readback and restore** test at low volume.
5. Compare a few distances on the receiver's own setup screen, particularly the subwoofer and a height channel. The diagnostic displays the raw unit and more precise step.

No calibration writes are needed for this test. The AVR-X4100W legacy-HTML reader is unchanged in its protocol handling. C# compilation and live device confirmation must be performed on Windows/.NET Framework 4.8.
