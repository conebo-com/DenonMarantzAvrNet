# Legacy HTTP / AVR-X4100W — read-only research

## Motivation

The AVR-X4100W user report (2026-10-20) confirms `Deviceinfo.xml`, `AppCommand.xml`, and `AppCommand0300.xml` on **port 80**, as well as a correctly correlated `CV?` response of `CVFL 50`, `CVFR 50`. The earlier `Not available for this receiver / profile` status of speaker calibration is produced by the **library profile**, not by a negative receiver reply.

## Evidence

- Denon X4100W manual: web-based **Speakers / Manual Setup / Levels / Distances** and Save/Load. UI existence does not alone prove an API.
- Oliver Goetz's public 2017 test XML script explicitly names the legacy MainZone XML endpoints and uses AppCommand on port 80; individual AppCommand commands still need receiver-side validation.
- The separate `Perkka2/DenonRemote` project publicly states X4100W live setup menu, speaker level, and distance access. Its reported support is independent evidence, **not a verified endpoint implementation in this library**. Consult that project's `docs/protocol.md` and license before using its specific wire format.

Reference links:
- https://manuals.denon.com/AVRX4100W/EU/EN/GFNFSYnkuzmnni.php
- https://github.com/ol-iver/misc_python_tools/blob/master/denonavr/denon_receiver_xml.py
- https://github.com/Perkka2/DenonRemote

## What this release does

`DenonAvrClient.DiscoverLegacyHttpAsync()` performs a fixed, finite, **read-only** inventory of documented, status-oriented endpoints on the detected HTTP port:

- GET `/` (web home page, extract names of a few interesting static references only)
- GET `/goform/Deviceinfo.xml`
- GET `/goform/formMainZone_MainZoneXml.xml`
- GET `/goform/formMainZone_MainZoneXmlStatus.xml`
- GET `/ajax/speakers/get_config?type=1` (optional modern feature probe)
- POST `/goform/AppCommand.xml` with `GetSurroundModeStatus`, `GetToneControl`, `GetRenameSource` status requests
- POST `/goform/AppCommand0300.xml` with `GetActiveSpeaker` and `GetInputSignal` status requests

The receiver's own HTML references are **never followed**. A `GET` endpoint with a name resembling a control command can change settings; guessing or automatically crawling these endpoints would be unsafe. Only XML **field paths**, root element names, and references to static pages are kept in the report; no XML field contents, MAC addresses, friendly names or volume values.

Per-request timeout is 3 seconds, with independent error classification. A negative request result is an endpoint-level finding only; it does not prove that speaker calibration cannot be read using another method.

`DenonTelnetClient.QueryRawChannelLevelsAsync()` returns every `CV?` line through `CVEND`, keeping unfamiliar native channel codes intact. The WPF compatibility report now contains both *CV? raw diagnostic* and *Legacy HTTP discovery (read only)*. This distinguishes a short receiver answer from a loss of rows in the channel parser.

## Next hardware test

1. Connect the AVR-X4100W with **automatic** `legacy-goform` detection.
2. Open the WPF compatibility report, select **Anonymized** and run the read-only tests.
3. Examine `CV? raw diagnostic`: did it end with `CVEND` after only `CVFL` and `CVFR`?
4. Examine `Legacy HTTP discovery`: which XML roots / field paths are returned, and which Speaker/Setup HTML references appear? Do not assume `Received` equals speaker calibration support.
5. Compare with the connected AVR's **Speaker Config** (and front display if convenient), without changing any calibration values.

Audio input format and sample rate can legitimately be unknown when no signal source is connected.

## Explicitly out of scope

- Legacy speaker level/distance **write** requests.
- Automatic traversal of receiver UI links.
- Restoring arbitrary setup backups.
- Interpreting unknown XML tags as channel-to-index mappings.
- Switching inputs or surround modes during discovery.

Do not claim X4100W calibration levels/distances supported until a concrete GET or read-only POST response is captured and parsed.

## Live speaker-matrix follow-up (AVR-X4100W report 2026-10-20)

The second read-only report confirms `CVFL 50`, `CVFR 50`, then `CVEND` with
`Response complete: True`. This means the **CV response itself contained two**
channel-level-adjustment entries. The receiver was in standby (`PWSTANDBY`).
Neither the CV count nor its 0 dB trims describes the calibration layout.

The same report confirms an XML `GetActiveSpeaker` structure at AppCommand0300
on HTTP port 80: `rx/cmd/name`, `rx/cmd/list/param`. Prior reports deliberately
omitted values/attributes, therefore neither the actual labels nor the slot
statuses are yet verified on the user's unit.

`DiscoverSpeakerMatrixAsync()` now reads the **GetActiveSpeaker status** through
the previously discovered AppCommand0300 endpoint and returns native parameter
IDs, optional labels, and **raw** `control` attributes in receiver order.
There is no numeric-index-to-`SpeakerChannel` conversion and no write.
The WPF compatibility report includes a new *Native speaker matrix (read only)*
operation. For privacy it prints only established short speaker tokens, never
arbitrary user-defined labels. Unknown control values are intentionally not
interpreted as configured vs. currently active.

If the response contains no entries, the result is `Empty` and **not** proof
that no speakers are connected. A timeout is `NoResponse`; an explicit Denon
error is `Rejected`. No status implies calibration GET/PUT availability.

### Still open

- The AVR-X4100W calibration levels and distances are visible in its setup UI,
  but the concrete HTTP setup GET/POST format has not yet been captured on the
  user's device. A successful `GetActiveSpeaker` matrix does not provide those.
- Receiver-control codes (`control=0/1/2/...`) and their meanings must be
  confirmed against the real UI; published examples are clues, not global rules.
- The original legacy `Deviceinfo.xml` sometimes times out in a short scan
  despite successful initialization: record per-request failures, not a global
  unsupported verdict.

Relevant references:
- https://manuals.denon.com/AVRX4100W/EU/EN/GFNFSYwwqdpazi.php
- https://manuals.denon.com/avrx4100w/eu/en/GFNFSYgntxjrhm.php
- https://github.com/Perkka2/DenonRemote (third-party claims about dynamic setup)
