# Changelog

## Unreleased (2026-10-20)

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
