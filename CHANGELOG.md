## Unreleased - priority sound / speaker diagnostics

## Legacy speaker calibration HTML reader (2026-10-20)

- Parse persistent calibration levels and speaker distances from the legacy SETUP/SPEAKERS HTML content frames via HTTP GET (tested against captured AVR-X4100W responses).
- Preserve native channel identifiers, display labels and original input fields without model-specific index tables.
- Parse selected distance unit (M/F), normalize to meters, and reject ambiguous/invalid responses.
- Add `GetLegacySpeakerCalibrationAsync()` and a separate read-only WPF support report operation.
- Add three offline tests for HTML parsing, malformed pages and GET-only transport. No write commands added.


- feat: native Movie/Music/Game/Direct/PureDirect family selection via Telnet and optional read-only OPSMLALL mode lists.
- feat: read-only `GetChLevel` per-input adjustment parser; separate from calibration speaker setup.
- fix: normalize Denon AJAX speaker distance units (`Unit=1` feet, `Unit=2` meters) and validate converted writes.
- test: extra regression checks for capabilities, raw channel adjustments, OPSMLALL and distance scaling.
- docs: document the remaining unverified AVR-X4100W persistent calibration level/distance endpoints.

﻿# Changelog

## Unreleased (2026-10-20)

### Dynamic DeviceInfo capabilities (2026-10-20)

- Add model-independent, read-only `DenonDeviceCapabilityDiscovery` with advertised command flags, 29 possible channel-type definitions on the sample X4100W, grouped Main Zone sound mode catalog and dynamic numeric limits.
- Parse and retain unusual fixed-range entries instead of assuming all channel definitions are connected/configured speakers.
- Probe advertised `GetChLevel`/`GetSoundModeList` with separate `AppCommand.xml` and experimental `AppCommand0300.xml` read-only envelopes; never call Set methods.
- Distinguish advertised, returned, empty, rejected and transport-error evidence; do not claim dynamic Audyssey calibration access.
- Extend anonymized WPF report with new operations, add three offline tests, and document protocol limitations in `docs/DYNAMIC-CAPABILITIES.md`.


- Exclude the project library from Costura embedding; ship the current DLL next to the WPF executable.
- Validate `AppCommandPort`/`AppCommand0300Port` public getters on WPF startup and report the actual loaded DLL path if the API is incompatible.
- Document the runtime diagnostics for cases where solution rebuild alone did not resolve `MissingMethodException`.

- Clarify generic Legacy (GoForm) profile labeling; AVR-X4100W is not a dedicated speaker AJAX profile.
- Document the `MissingMethodException`/mixed assembly issue and the clean rebuild procedure for the Costura-enabled WPF tester.

- Keep the receiver-detected profile as the tester default; allow explicit `legacy-goform` and generic profiles without pretending that an X4100W is an X6700H.
- Discover AppCommand and AppCommand0300 on their actual HTTP ports instead of deciding availability from port 8080 alone.
- Separate AppCommand failure from basic legacy MainZone status; retain read-only fallbacks.
- Share one Telnet TCP session per host between command clients and event listeners, serialize requests and filter unrelated status messages from command replies.
- Add read-only port-80 HTTP and local fake-receiver Telnet regression tests and diagnostics documentation.
- Preserve existing speaker setup readers, writers, and device-specific mappings; no new unverified speaker writes.

### Legacy HTTP investigation (2026-10-20)

- Add fixed, read-only Legacy HTTP/XML endpoint inventory for AVR-X4100W-era receivers (no model-specific profile needed for the probe).
- Add complete raw `CV?` diagnostic through `CVEND`, independent of speaker channel name mappings.
- Extend WPF compatibility report with both diagnostics; XML field **names** only to avoid exposing private values.
- Add two offline regression checks and research notes in `docs/LEGACY_HTTP_RESEARCH.md`.
- No changes to speaker calibration readers/writers, volume, or sound mode controls.

### Native speaker matrix (2026-10-20)

- Parse the receiver-provided GetActiveSpeaker/AppCommand0300 native slots on HTTP 80 or 8080, without a model speaker profile.
- Keep parameter IDs, optional labels and raw control codes; do not infer Audyssey calibration indices or active output semantics.
- Expose `DiscoverSpeakerMatrixAsync()` and include a privacy-conscious read-only operation in WPF support reports.
- Add parser/HTTP port 80 tests and update legacy speaker research notes.
- Reconfirm CV? returned exactly two adjustments at standby; no setup/calibration writes were added.

## Integrated sound modes and persistent calibration (2026-10-20)

- Added unified `GetSpeakerCalibrationAsync()` for legacy speaker-setup HTML and modern speaker AJAX, with canonical native channel codes and independent level/distance indices.
- Added guarded `SetCalibrationLevelAsync()` and `SetCalibrationDistanceAsync()` with fresh page reads, one-channel updates and mandatory readback. Legacy HTML POST requires explicit opt-in; its live write behavior has **not** yet been verified on the AVR-X4100W.
- Added `SelectSoundModeFamilyAndReadBackAsync()` to distinguish a family button request from the actual receiver sound mode (e.g. `MSGAME` responding `MSDTS NEO:X G`).
- Shortened optional `OPSMLALL` timeout to three seconds, retaining the separately advertised sound mode catalog for older receivers.
- Added WPF one-pass sound family test with attempted restoration, a unified speaker calibration view and explicit confirmation for persistent writes.
- Fixed legacy HTML speaker label extraction and canonical SW2/SW3/SW4 speaker naming; added offline payload and mapping checks.
- .NET Framework 4.8 remains the sole target. Windows builds and hardware WRITE tests are required before claiming complete write support.

## Final sound-mode and calibration verification (2026-10-20)

- Verify sound-mode restoration with an exact `MS?` comparison (up to three reads); report failure when the restored mode differs.
- Add explicitly confirmed one-pass single-channel calibration level test (+/-0.5 dB) with best-effort restoration and checks of unchanged channels.
- Retry speaker calibration readback up to three times before deciding an HTTP write remains unverified.
- Add regression coverage for exact mode restoration; update the integration documentation.
- Preserve the .NET Framework 4.8 target and keep persistent writes out of read-only compatibility reports.
