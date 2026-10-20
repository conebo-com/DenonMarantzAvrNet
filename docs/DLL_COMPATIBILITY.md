# GUI/library assembly compatibility (.NET Framework 4.8)

`System.MissingMethodException: DenonAvrClient.get_AppCommandPort()` means the running tester was compiled against a library API that is missing from the **loaded** `DenonMarantzAvrNet.dll`. The current source defines `public int? AppCommandPort { get; private set; }`; the issue is a mixed or stale binary deployment, not an unavailable receiver feature.

The WPF tester references the library using a project reference and uses `Costura.Fody`, which can embed dependency assemblies in the executable. Updating a single DLL or launching an older tester EXE is not a reliable upgrade procedure.

## Clean rebuild (Windows / Visual Studio)

1. Exit every running tester process.
2. Open the **same solution/source revision** for the library and tester.
3. Select **Build > Clean Solution**.
4. Delete `bin` and `obj` from `DenonMarantzAvrNet`, `DenonMarantzAvrNet.TesterGui`, and `DenonMarantzAvrNet.Tests` (after closing Visual Studio if files are locked).
5. Reopen Visual Studio as necessary, restore NuGet packages, then **Build > Rebuild Solution**.
6. Run the freshly built tester from the selected configuration, not an older published/copy-pasted EXE. The EXE and its loaded library must be from the same build.

If the error persists, use the debugger's **Debug > Windows > Modules** window to inspect the actual loaded `DenonMarantzAvrNet` assembly location (embedded Costura assemblies may have special paths). Verify its origin and clean all prior output folders.

A direct DLL check in Windows PowerShell (point the path at the **actual current build**, not at the old EXE folder):

```powershell
$dll = 'C:\path\to\bin\Debug\net48\DenonMarantzAvrNet.dll'
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$assembly.GetType('DenonMarantzAvrNet.DenonAvrClient').GetProperty('AppCommandPort')
```

A non-null result verifies this API exists in that particular DLL. It does **not** by itself prove that the running WPF EXE is loading that DLL.

## Profile terminology

`legacy-goform` is a **generic fallback** for older HTTP GoForm-capable receivers, including the AVR-X4100W as currently detected. It is **not** a tested AVR-X4100W-specific speaker setup profile. Keep automatic detection enabled unless you intentionally test a verified, compatible profile.

## Runtime guard and embedding policy (2026-10-20)

`DenonMarantzAvrNet.TesterGui/FodyWeavers.xml` excludes `DenonMarantzAvrNet`
from Costura. After a new build the executable **must be deployed together**
with the `DenonMarantzAvrNet.dll` copied to its output directory. Other
third-party dependencies may still be embedded. The tester checks the
`Nullable<int>` getters `AppCommandPort` and `AppCommand0300Port` before
opening the main window. If one is missing, the error dialog reports the
**actual** assembly location. This is intentional: no stale library should
be silently accepted, and no incompatible API should be silently suppressed.

When a clean rebuild was already attempted, do **not** simply suggest cleaning
again. Instead inspect the path in the startup dialog (or use Visual Studio's
Debug > Windows > Modules) and compare the DLL in the launched EXE folder
with the DLL produced by the library project. Verify project references and
startup configuration. If the loaded DLL is not the output of the same build,
fix the launch/deployment path.

PowerShell check (after launching from the desired output directory):

```powershell
$dll = 'C:\path\to\DenonMarantzAvrNet.TesterGui\bin\Debug\DenonMarantzAvrNet.dll'
$a = [Reflection.Assembly]::LoadFrom($dll)
$a.Location
$a.GetType('DenonMarantzAvrNet.DenonAvrClient').GetProperty('AppCommandPort')
```

A legacy Denon receiver profile is a protocol fallback, not a verified model
profile. This fix does not establish support for undocumented speaker AJAX
requests on the AVR-X4100W.
