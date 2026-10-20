# Speaker indices: read-only dynamic discovery

## Purpose

`DenonAvrClient.DiscoverSpeakerIndicesAsync()` queries the selected speaker-AJAX
profile without using Telnet and **without sending any write requests**.
It provides an inventory of the native numeric IDs currently returned by the AVR.

| HTTP endpoint | Result in report | Interpretation |
| --- | --- | --- |
| `/ajax/speakers/get_config?type=5` | `LevelEntries` | Native speaker-level page indices, raw tenths-of-dB values |
| `...type=4` | `DistanceEntries`, `DistanceUnitRaw`, `DistanceStepRaw` | Native distance page indices and *uninterpreted* raw distance metadata |
| `...type=1` | `MenuItems` | Menu names and raw availability codes |
| `...type=3` | `ConfigurationEntries` | SpeakerConfig indices, **not automatically compatible with level indices** |
| `...type=15` | `LayoutGroupEntries` | Layout group indices (`Item`), **not physical speaker indices** |

The reader presently supports the configured AJAX transports of the `avc-x6800h`
and `avc-x6700h` profiles. On a legacy receiver or unknown profile, it raises
`NotSupportedException` until a suitable read-only transport is established.
Unsupported individual AJAX pages are recorded in `Warnings`; they do not prevent
partial results from other successful reads.

### Source versus interpretation

- `NativeIndex`, `RawValue`, `DisplayCode`, `ReceiverLabel` are taken from the XML.
  A null `ReceiverLabel` means the XML had no explicit label.
- `ProfileChannelHint` comes from the **existing X6800H mapping tables**, *not*
  from the AVR. The new method returns no index mapping hints for the X6700H
  until its own tables have been verified.
- Index `1` in a level response **need not be the same speaker** as index `1`
  in a distance or configuration response.
- Menu `display`/status numbers such as `1`, `2`, `3` are not speaker IDs.
- Presence in a levels/distances page does not prove a channel is configured
  or actively used for playback.
- Distance unit codes are retained **raw** here because public community
  examples conflict with the library's previous unit assumptions. Do not
  derive safe write units or conversions from this discovery method alone.

## Testing on hardware (no modification of settings)

1. Open `DenonMarantzAvrNet.TesterGui` and connect to the receiver.
2. Select **Speakers (HTTP) → Discover native speaker indices (read-only)**.
3. Run the command once on each of AVC-X6800H and AVR-X6700H.
4. Compare the reported indices against Setup → Speakers → Levels / Distances.
5. Save the result and corresponding raw XML, but review and redact IPs,
   identifiers and setup-sensitive values before sharing it publicly.
6. A warning for `type=3` or `type=15` means **unknown/temporarily inaccessible**, not
   that the model lacks speakers or that its indices are unsupported.

Programmatic use:

```csharp
var inventory = await client.DiscoverSpeakerIndicesAsync();
foreach (var entry in inventory.LevelEntries)
{
    Console.WriteLine($"level index {entry.NativeIndex}, raw {entry.RawValue}, " +
        $"receiver label {entry.ReceiverLabel ?? "unknown"}");
}
```

### Future work

Investigate the receiver-hosted web application's JavaScript index dictionaries,
but only consume them after their origin, firmware applicability and channel
semantics can be verified. Before changing current speaker writes, independently
validate distance unit encoding and add write/readback safeguards.
