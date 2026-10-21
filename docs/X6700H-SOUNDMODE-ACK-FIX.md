# AVC-X6700H – Sound mode family acknowledgement and readback fix (2026-10-21)

## Hardware observation (automatic X6700H profile)

The October 21 one-pass test verified that modern speaker AJAX can change `FL` from
-4.0 to -3.5 dB, read the changed value, and restore -4.0 dB. All 14 channels and
14 distances read correctly. This patch does **not** modify calibration providers.

The same test returned `OperationCanceledException` for `MSMOVIE`, `MSMUSIC` and
`MSGAME`, while `MSDIRECT`, `MSPURE DIRECT`, and the original `MSSTEREO` restoration
worked. A timeout waiting for a SET response is *not* proof that the receiver ignored
the requested command.

## Implementation

- Family selection in `SelectSoundModeFamilyAndReadBackAsync` now sends the `MS` family
  command on the serialized Telnet session **without requiring an immediate SET echo**.
  TCP write completion is reported only as *sent*, never as *acknowledged*.
- The client captures matching `SSSMG MOV`, `SSSMG MUS`, `SSSMG GAM` family events (if
  emitted) independently of the concrete playback-mode status from `MS?`.
- Up to six `MS?` readback attempts allow receiver state changes to settle. A failed
  optional readback does not automatically turn into a false SET-command failure.
- `DenonSoundModeSelectionResult.Verification` reports:
  - `Confirmed`: a matching family event **plus** an MS status, or exact `MSDIRECT`
    / `MSPURE DIRECT` response for those specific commands.
  - `ModeChangedFamilyUnconfirmed`: the reported MS mode changed, but no matching
    family event was observed. This is **not** counted as a confirmed family.
  - `Unchanged`: same mode, no matching family event; not proof the command failed
    (the family can legitimately keep the same mode).
  - `Unverified`: no usable fresh MS status.
- `SSSMG PUR` does **not** distinguish Direct from Pure Direct on its own; these
  require their respective exact MS mode status for positive confirmation.
- The WPF one-pass test displays command, MS readback, optional `SSSMG` message and
  classification separately. The final restore sends the original mode without
  requiring a SET echo, then verifies the **exact** original `MS?` state. Failed
  recovery is explicitly reported; it is never marked successful merely because
  the command was transmitted.
- The shared Telnet session keeps requests serialized. If a command that still
  requires a response times out, it closes the old connection. Lines received by
  the retired reader are discarded so they cannot satisfy a new request.

## Scope and safety

`DenonTelnetClient.SelectSoundModeFamilyAsync` uses send-then-readback instead of
waiting for an unsolicited SET echo. `DenonTelnetClient.SendSurroundModeWithoutAckAsync`
is intentionally available for explicit recovery, but the caller **must** validate
that the desired mode was actually applied. `DenonAvrClient` exposes the richer
selection result and diagnostic status.

Speaker setup, AJAX/HTML calibration, channel mapping, and model/profile selection
are **unchanged**. No speaker-calibration writes are performed by sound mode tests.
`OPSMLALL` is still an optional, not hardware-verified list protocol. Writing speaker
distances remains a separate unverified hardware test.

## One final targeted hardware test

1. On AVC-X6700H with **Auto** profile, choose a low volume and note the current mode.
2. In the WPF tester run `ONE-PASS Sound family test (switch and restore)` once.
3. Report the five per-family `verification=` lines and the final `RESTORE VERIFIED`
   or `WARNING: RESTORE NOT VERIFIED` line. In particular, if a family has no
   `SSSMG` message, a changed `MS?` value by itself is not a definitive family proof.
4. Do **not** run all compatibility reports or calibration writing tests again.

## Regression checks

- Family-event classification with unchanged, changed and missing codec-status cases.
- Simulated no-echo family command followed by `SSSMG MOV`, `MS?` and a subsequent
  unrelated `PW?` over the same socket.
- A timed-out pending response followed by a new connection and clean MS? query.

The project remains `net48`. A Windows SDK build and live receiver verification are
required to confirm the new behavior; source-level/ZIP/patch checks are not a build.

## Follow-up hardware observation: first MOVIE press

On AVC-X6700H, the user observed that the first press of MOVIE on the physical
remote can leave `MSSTEREO` active while opening the on-screen sound-mode list;
subsequent presses cycle through available entries. A one-pass test yielded
`MSMOVIE` / `MSSTEREO` without `SSSMG MOV`, so this remains **Unchanged / not
confirmed**, not a failure to send the command and not positive proof of family
selection. The WPF tester now explains this possibility only for Movie. It does
not automatically send MOVIE twice, since doing so could change playback mode.
The other observed results were Music and Game with matching SSSMG events,
Direct and Pure Direct with exact MS statuses, and exact Stereo restoration.
