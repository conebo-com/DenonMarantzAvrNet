# Integrated calibration and sound mode operation

## Design goal

One `net48` public API works with different persistent speaker-setup transports:

- AVR-X4100W / compatible *legacy HTML*: `/SETUP/SPEAKERS/LEVELS/d_speakersetup.asp` and `/SETUP/SPEAKERS/DISTANCES/d_speakersetup.asp` over HTTP GET. All fields and native channel IDs are enumerated dynamically, not hardcoded by AVR model.
- AVC-X6800H / AVR-X6700H *modern AJAX*: the existing profile-selected speaker-level and speaker-distance providers. These have different **index spaces** and must be joined by their channels, never by matching indices.

`GetSpeakerCalibrationAsync()` returns `DenonSpeakerCalibrationSnapshot` with one `DenonCalibrationChannel` per channel, nullable level/distance values, native channel string (`FL`, `TML`, `SW2`, etc.), and separate modern level/distance indices where available. Unknown native indices are preserved as `LEVEL#N` or `DISTANCE#N`; they are **not** silently matched.

`CV?` and `GetChLevel` report OPTION/per-input channel trims and are **not** substitutes for persistent speaker setup. Modern speaker *presets* and legacy speaker setup are also different concepts.

## Sound mode families

`SelectSoundModeFamilyAndReadBackAsync(family)` sends the receiver remote-button family command (Movie/Music/Game/Direct/Pure Direct), then reads `MS?`. The response to `MSGAME` may be `MSDTS NEO:X G`—this is a legitimate *currently selected* listening mode, not evidence of command failure. No particular codec is assumed for a family.

`GetAdvertisedSoundModeCatalog()` gives the receiver's `Deviceinfo.xml` static Main Zone mode catalog; this is **not** a signal-dependent per-family list. `OPSMLALL?` offers detailed runtime mode lists only where supported and now stops after three seconds. On an AVR-X4100W the query has not been verified.

The WPF `ONE-PASS Sound family test (switch and restore)` is explicitly confirmed and records actual responses for each family, attempting to restore the original mode afterwards. A change to speaker configuration or calibration is never part of that test.

## Write safety

**No persistent settings are written during the compatibility report.**

The new setters are `SetCalibrationLevelAsync(nativeCode, dB, allowExperimentalLegacyWrite, token)` and `SetCalibrationDistanceAsync(nativeCode, meters, allowExperimentalLegacyWrite, token)`.

- Validation: channel must exist in a fresh snapshot of the relevant page. Levels are -12..+12 dB in 0.5 dB increments; distances are finite and greater than zero, at most 18 m.
- Modern receiver: resolve the index from the correct live page and call the existing AJAX provider.
- Legacy receiver: throw `NotSupportedException` unless `allowExperimentalLegacyWrite: true`; the WPF GUI asks for explicit confirmation with the old and new value.
- Legacy form payload is based on a fresh GET of the exact receiver web page, retaining other channels, unit selection, and submit flag. Calibration level submission sets `setCLA=Set` as observed in the captured script; distance submission uses the captured `setDelayTimeAllSet=off` form field. Only one channel is altered in the payload.
- Mandatory GET readback follows a write. If value differs, an exception warns that the write **was not confirmed**. Readback cannot guarantee that no unrelated setting was affected, which is why the feature remains experimental until hardware verification.

**Important:** The X4100W **read** path has been tested against a physical receiver (9 levels, 9 distances). The X4100W **write** form has not been tested on hardware. Capture/backup existing setup before deliberately using any experimental write. Do not promise atomic writes or automatic rollback.

## Final smoke test (one pass)

1. Build the full solution with Visual Studio targeting .NET Framework 4.8.
2. Connect the AVR-X4100W with automatic profile selection.
3. In the WPF GUI run `Calibration (AUTO: HTML / AJAX, read only)` and `ONE-PASS Sound family test (switch and restore)`. Review actual `MS?` readback and restored mode.
4. Run the compatibility report once to validate the read-only results; share it only after anonymization.
5. Optionally, **after backup**, test one explicitly confirmed level or distance write and verify the original value can be restored. This step is **not required** to validate reads and sound families.

Actual Windows compilation and receiver write behavior are not asserted by these source-level changes. Other legacy receiver web structures require separate evidence before blanket compatibility can be claimed.

## Final hardware verification (2026-10-20)

The AVR-X4100W Telnet capture confirms all five remote families: Movie -> Dolby Surround,
Music -> Stereo, Game -> DTS Neo:X G, Direct -> Direct, Pure Direct -> Pure Direct.
Sound family switches are independent of the selected codec. `OPSMLALL` remains an
optional unverified feature on this legacy model.

The one-pass mode test now sends the original `MS` mode and retries `MS?` up to three
times. It reports **RESTORE VERIFIED** only if the full returned `MS` status matches
the original. A different `MS...` message is never counted as restored.

The earlier X4100W HTTP log shows POST submissions for levels/distances, but the
values submitted were unchanged, so it cannot prove real write capability. The GUI
now offers **ONE-PASS calibration level test (+/-0.5 dB, auto-restore)**. It uses
one named native channel (default `FL`), requires explicit confirmation, temporarily
changes the value by precisely 0.5 dB, verifies the readback, then tries to restore
the original value. It also compares the remaining levels with the baseline. No
write is included in the compatibility report.

**Caution:** Restoration is best effort, not transactional. If network/power is lost,
the device rejects a write, or the process is closed, the user must verify and
possibly restore the saved speaker setup manually. A changed value is not called
verified without readback, and a successful HTTP POST does not establish persistence.
The setter now retries live readback up to three times to tolerate delayed device
updates. Distance writing remains guarded/experimental on legacy units until a
real changed distance has been confirmed and restored.
