# Sound modes, channel trims, calibration levels and distances (.NET Framework 4.8)

## Sound modes

- `SelectSoundModeFamilyAsync(DenonSoundModeFamily.Movie|Music|Game|Direct|PureDirect)` uses Telnet's native `MSMOVIE`, `MSMUSIC`, `MSGAME`, `MSDIRECT`, `MSPURE DIRECT` commands. It **changes** the AVR mode and only runs at the user's request.
- The catalog's `SoundModeFamilies` comes from the main-zone `Deviceinfo.xml/ShortcutControl/EntryList` Category 5 entries, **not** from the sometimes malformed `SoundMode/Genre` nodes. These are advertised buttons, not guaranteed playback-compatible modes.
- `GetSoundModeOptionsAsync()` uses the optional, **read-only** `OPSMLALL?` query and waits for `OPSMLALL END` to assemble a native per-category list. Legacy devices may not support this query. A timeout is not an empty supported list. The parser preserves native labels and status; it does not fabricate protocol commands for those labels.
- The old `SetSurroundModeAsync(string)` remains available for individual sound modes.

## Three different level settings (do not conflate)

1. **Channel Level Adjust** (receiver remote OPTION menu): source-specific playback trim. HTTP `GetChLevel` and Telnet `CV?` are *not* the stored setup calibration. `GetChannelLevelAdjustmentsAsync()` reads `AppCommand.xml` and retains native `status/sptype/level/value` without guessing units or exposing private XML.
2. **Speaker Setup -> Levels**: persistent calibration. Existing modern AJAX Speaker Preset APIs are unchanged (`GetSpeakerPresetLevelsAsync` etc.). The AVR-X4100W's read/write calibration endpoint is still **unknown**, so the legacy profile intentionally refuses writes.
3. **Input -> Source Level**: gain normalization for an entire input; not implemented here.

## Speaker distances

Modern AJAX `get_config?type=4` uses raw hundredths of the **selected unit** (`Unit=1` feet, `Unit=2` meters) according to documented Denon AJAX captures. Conversion now always returns SI meters and converts incoming meter values back to the current unit before any write. Writes validate the returned list and reject nonexistent indices or measurements over 18 m. **Compare actual read values with the AVR on-screen menu before trusting or using writes on your model/firmware.**

AVR-X4100W legacy setup distances are **not** currently accessible through a verified protocol in this library. `GetChLevel` does not substitute for them. A separate capture of read requests made by the legacy web/setup interface is needed; no speculative SET requests are performed.

## Hardware acceptance tests

- X4100W: With receiver on, test Sound family Movie/Music/Game/Direct. Check displayed effect; no input signal may limit selectable modes. Run the read-only OPSMLALL and GetChLevel tests; compare raw values to OPTION menu; do not mistake them for Audyssey speaker levels.
- X6800H: Read the native `Unit` and distance data. Compare FL, C, FR distances to the on-screen setup. `Unit=1` changes previously misinterpreted values (e.g., `971` means **9.71 feet**, approximately 2.96 meters). Only attempt a distance write after this check and after saving existing values.
- X4100W persistent calibration levels/distances: still pending a validated read endpoint. The old GUI's `Not available for this receiver / profile` status is accurate for these functions; it says nothing about the device's physical capabilities.
- Execute all offline tests and build all projects under Windows .NET Framework 4.8. No Windows build environment is available in this container.
