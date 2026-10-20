# HTTP discovery and shared Telnet session (2026-10-20)

## Scope and receiver evidence

This change is based on the uploaded AVC-X6800H and AVR-X4100W compatibility reports. The X4100W reported model `*AVR-X4100W` and `legacy-goform`, but the GUI previously forced a manual X6700H speaker profile. The X6800H returned several mismatched Telnet command/report pairs (e.g., `PW?` -> `DCAUTO`, `MU?` -> `MVMAX 755`). These are symptoms of the previous "return the first received line" implementation, not evidence that the receiver implements those commands incorrectly.

### Profile selection

The GUI now defaults to the detected profile. `legacy-goform` and `unknown` are explicit options alongside X6800H and X6700H. Selecting a different profile remains an *explicit compatibility override*. On the X4100W this prevents accidental use of the unverified X6700H speaker-AJAX HTTPS port. Speaker-setup operations without a verified provider report unavailable rather than attempting an unrelated model's request.

### HTTP discovery

`InitializeAsync` tries `Deviceinfo.xml` on ports 80 and 8080. **After** successful identity detection it independently probes `AppCommand.xml` on both ports by posting a read-only `GetAllZonePowerStatus`. `SupportsAppCommand` is set from a nonempty XML `<rx><cmd>...</cmd></rx>` response, not a port comparison. `AppCommandPort` preserves the port that responded. A failed probe does not change the detected model or make the entire receiver unavailable. Probe timeout is bounded to 3 seconds per port.

`UpdateAsync` uses the verified AppCommand port where available and otherwise falls back to legacy MainZone status. `GetDeletedSource` is optional: if its AppCommand response is unavailable, `RefreshInputsAsync` can fall back to the legacy MainZone input list. Audio details use a separate optional `AppCommand0300.xml` read-only probe across both known ports; the successful port is retained in `AppCommand0300Port`. Failed 0300 probes leave basic status intact. They do not establish permanent hardware absence.

Deviceinfo identity, AppCommand availability, AppCommand0300 availability, Telnet support and speaker-AJAX profile support are **separate capabilities**. No additional speaker-setup write functionality was enabled.

### Telnet session

Multiple instances of `DenonTelnetClient` and `DenonTelnetEventListener` for the same host now share one TCP connection (port 23) through `DenonTelnetSession`. Requests are serialized; one background reader consumes incoming lines. The response matcher checks the expected protocol prefix (`PW`, `MV`, `MU`, `SI`, `MS`, `DC`, `Z2`, `Z3`, and `CV`). In particular, `MVMAX` is NOT accepted as the response to `MV?`. `CV?` collects channel messages through `CVEND`.

Other status messages continue to be published on the shared event stream. Denon's protocol has no request identifiers: a matching status message is evidence of a plausible result, **not** an unambiguous acknowledgement or a verified state change. Commands that do not yield recognizable status responses can time out. When a query times out or the connection fails, the session is closed and subsequent calls reconnect; the event listener also attempts reconnects. Avoid treating an unconfirmed write as definitely unsuccessful or automatically repeating it.

**Lifecycle note:** Sessions are process-scoped per host to allow independent library/tester/monitor clients to share the same connection. Detaching the last event listener closes an idle connection; command-only sessions remain connected for reuse and are released when the application process exits. A future iteration can add explicit ownership and idle eviction.

## Windows offline checks

Requires .NET Framework 4.8 targeting pack and suitable SDK:

```powershell
dotnet build .\DenonMarantzAvrNet\DenonMarantzAvrNet.csproj
dotnet build .\DenonMarantzAvrNet.TesterGui\DenonMarantzAvrNet.TesterGui.csproj
dotnet run --project .\DenonMarantzAvrNet.Tests\DenonMarantzAvrNet.Tests.csproj
```

The offline console tests cover the X4100W profile, port-80 AppCommand discovery with mocked responses, validity of XML capability probes, Telnet response-prefix matching and a local TCP fake receiver sending unsolicited data before the expected `PW?`, `MV?`, and `CV?` responses. No receiver writes are performed.

## Hardware test (read-only)

1. Connect to the AVR-X4100W and leave **Auto (`legacy-goform`)** selected. Do not select `avc-x6700h` as a substitute.
2. Run **Check receiver compatibility** with an anonymized report. Note `AppCommand port` and the status `SupportsAppCommand`. Missing speaker AJAX capabilities should show warnings rather than an HTTPS/10443 transport error.
3. Repeat on the AVC-X6800H. Confirm the returned replies start with the expected prefixes: `PWON`, `MV...` (not `MVMAX`), `MU...`, `SI...`, `MS...`, `DC...`, `Z2...`, `Z3...`. `CV?` must still end with `CVEND` internally.
4. Compare read-only audio fields and HTTP ports. A valid legacy MainZone status with missing 0300 audio fields is not a connection failure.
5. Keep speaker distance *writes* disabled until raw `Unit` codes and conversions have been verified against the AVR's own setup display.

Do not publish host IPs, MAC addresses or full unredacted diagnostic logs.
