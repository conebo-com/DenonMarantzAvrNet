using System.Globalization;

namespace DenonMarantzAvrNet.TesterGui
{
    internal static class Loc
    {
        private static readonly Dictionary<string, string> German = new()
        {
            // MainWindow
            ["Denon / Marantz AVR Tester"] = "Denon / Marantz AVR-Tester",
            ["Connect"] = "Verbinden",
            ["Open receivers web page"] = "Receiver-Webseite öffnen",
            ["Language:"] = "Sprache:",
            ["Not connected"] = "Nicht verbunden",
            ["Connecting ..."] = "Verbinde ...",
            ["Connected: {0}"] = "Verbunden: {0}",
            ["HTTP initialization failed (Telnet commands may still work)"] = "HTTP-Initialisierung fehlgeschlagen (Telnet-Befehle funktionieren evtl. trotzdem)",
            ["Compatibility mode: AVR reports '{0}', using profile '{1}'"] = "Kompatibilitätsmodus: Der AVR meldet '{0}', verwendet wird Profil '{1}'",
            ["Protocol"] = "Protokoll",
            ["Result"] = "Ergebnis",
            ["HTTP output"] = "HTTP-Ausgabe",
            ["Telnet output"] = "Telnet-Ausgabe",
            ["Clear HTTP / Telnet output before every operation"] = "HTTP-/Telnet-Ausgabe vor jeder Operation leeren",
            ["Check receiver compatibility..."] = "Receiver-Kompatibilität prüfen...",
            ["Help the project: check whether your receiver is compatible with the functions offered here. Only read-only operations are run against your receiver, and you can send the result to the project by e-mail. Nothing is sent automatically and nothing is changed on the receiver."] = "Unterstützen Sie das Projekt: Prüfen Sie, ob Ihr Receiver mit den hier angebotenen Funktionen kompatibel ist. Dazu werden nur reine Leseoperationen gegen Ihren Receiver ausgeführt, und das Ergebnis können Sie dem Projekt per E-Mail schicken. Es wird nichts automatisch gesendet und am Receiver nichts verändert.",
            ["Clear outputs"] = "Ausgaben leeren",
            ["Send"] = "Senden",
            ["Enter a host or IP address."] = "Geben Sie einen Host oder eine IP-Adresse ein.",
            ["Select a command first."] = "Wählen Sie zuerst einen Befehl aus.",
            ["Running '{0}' ..."] = "'{0}' wird ausgeführt ...",
            ["Connect to a receiver first."] = "Verbinden Sie sich zuerst mit einem Receiver.",
            ["[Log read error] {0}"] = "[Fehler beim Lesen des Logs] {0}",

            // Command groups
            ["Main Zone"] = "Hauptzone",
            ["Zones 2/3"] = "Zonen 2/3",
            ["Speakers (HTTP)"] = "Lautsprecher (HTTP)",
            ["Audio / Levels (Telnet)"] = "Audio / Pegel (Telnet)",

            // Parameter labels
            ["Zone (2 or 3)"] = "Zone (2 oder 3)",
            ["State (on/off)"] = "Zustand (on/off)",
            ["Speaker index"] = "Lautsprecher-Index",
            ["Volume in dB (-80.0 to +18.0)"] = "Lautstärke in dB (-80,0 bis +18,0)",
            ["Input name (e.g. Media Player, TV AUDIO, MPLAY)"] = "Eingangsname (z. B. Media Player, TV AUDIO, MPLAY)",
            ["Direction (up/down)"] = "Richtung (up/down)",
            ["Input name"] = "Eingangsname",
            ["Level in dB (-12.0 to +12.0)"] = "Pegel in dB (-12,0 bis +12,0)",
            ["Distance in meters"] = "Distanz in Metern",
            ["Preset number"] = "Preset-Nummer",
            ["Channel (e.g. FrontLeft, Subwoofer)"] = "Kanal (z. B. FrontLeft, Subwoofer)",
            ["Mode (Auto, Stereo, Dolby Surround, DTS Neural:X, Multi Ch Stereo, Pure Direct)"] = "Modus (Auto, Stereo, Dolby Surround, DTS Neural:X, Multi Ch Stereo, Pure Direct)",
            ["Mode (Auto, PCM, DTS)"] = "Modus (Auto, PCM, DTS)",
            ["Command (e.g. PW?, MV?, SI?)"] = "Befehl (z. B. PW?, MV?, SI?)",

            // Command names
            ["Refresh status"] = "Status aktualisieren",
            ["Receiver info"] = "Receiver-Info",
            ["Probe Telnet (updates capabilities)"] = "Telnet prüfen (aktualisiert Fähigkeiten)",
            ["Power on"] = "Einschalten",
            ["Volume up"] = "Lauter",
            ["Volume down"] = "Leiser",
            ["Set volume (dB)"] = "Lautstärke setzen (dB)",
            ["Mute on"] = "Stumm ein",
            ["Mute off"] = "Stumm aus",
            ["List available inputs"] = "Verfügbare Eingänge anzeigen",
            ["Select input"] = "Eingang wählen",
            ["Power (Telnet)"] = "Ein-/Ausschalten (Telnet)",
            ["Volume up/down"] = "Lauter/Leiser",
            ["Mute"] = "Stumm",
            ["Select input (Telnet)"] = "Eingang wählen (Telnet)",
            ["Configured speakers (levels + distances)"] = "Konfigurierte Lautsprecher (Pegel + Distanzen)",
            ["Preset levels"] = "Preset-Pegel",
            ["Distances"] = "Distanzen",
            ["Set preset level"] = "Preset-Pegel setzen",
            ["Set distance"] = "Distanz setzen",
            ["Read active preset"] = "Aktives Preset lesen",
            ["Switch preset (can take ~15 s)"] = "Preset wechseln (kann ca. 15 s dauern)",
            ["Channel levels"] = "Kanalpegel",
            ["Set channel level"] = "Kanalpegel setzen",
            ["Surround mode"] = "Surround-Modus",
            ["Digital input decoder"] = "Digitaler Eingangsdecoder",
            ["Raw Telnet command"] = "Roher Telnet-Befehl",

            // Results
            ["Command sent. This command returns no response text."] = "Befehl gesendet. Dieser Befehl liefert keinen Antworttext.",
            ["Telnet response: {0}"] = "Telnet-Antwort: {0}",
            ["Power:        {0}"] = "Betrieb:      {0}",
            ["Input:        {0}"] = "Eingang:      {0}",
            ["Volume:       {0}"] = "Lautstärke:   {0}",
            ["Mute:         {0}"] = "Stumm:        {0}",
            ["Audio input:  {0}"] = "Audioeingang: {0}",
            ["Audio format: {0}"] = "Audioformat:  {0}",
            ["Sound mode:   {0}"] = "Klangmodus:   {0}",
            ["Sample rate:  {0}"] = "Abtastrate:   {0}",
            ["Speakers:     {0}"] = "Lautsprecher: {0}",
            ["{0}:       {1}, {2}, {3}, Mute {4}"] = "{0}:       {1}, {2}, {3}, Stumm {4}",
            ["unknown"] = "unbekannt",
            ["unknown/none"] = "unbekannt/keine",
            ["Unknown"] = "Unbekannt",
            ["On"] = "Ein",
            ["Off"] = "Aus",
            ["HTTP port:    {0}"] = "HTTP-Port:    {0}",
            ["Profile:      {0}"] = "Profil:       {0}",
            ["Device info:  {0}"] = "Geräteinfo:   {0}",
            ["Capabilities: {0}"] = "Fähigkeiten:  {0}",
            ["The receiver returned no inputs."] = "Der Receiver hat keine Eingänge zurückgegeben.",
            ["The receiver reported no configured speakers."] = "Der Receiver hat keine konfigurierten Lautsprecher gemeldet.",
            ["Channel"] = "Kanal",
            ["Level"] = "Pegel",
            ["Distance"] = "Distanz",
            ["The receiver returned no preset levels."] = "Der Receiver hat keine Preset-Pegel zurückgegeben.",
            ["Receiver unit: {0}, step: {1}"] = "Receiver-Einheit: {0}, Schritt: {1}",
            ["Active speaker preset: {0}"] = "Aktives Lautsprecher-Preset: {0}",
            ["Preset {0} confirmed by the receiver."] = "Preset {0} vom Receiver bestätigt.",
            ["The receiver returned no configured channel levels."] = "Der Receiver hat keine konfigurierten Kanalpegel zurückgegeben.",
            ["{0}: {1} set."] = "{0}: {1} gesetzt.",
            ["'{0}' is not a valid number."] = "'{0}' ist keine gültige Zahl.",
            ["'{0}' is not a valid integer."] = "'{0}' ist keine gültige Ganzzahl.",
            ["The zone must be 2 or 3."] = "Die Zone muss 2 oder 3 sein.",
            ["Enter 'on' or 'off'."] = "Geben Sie 'on' oder 'off' ein.",
            ["Enter 'up' or 'down'."] = "Geben Sie 'up' oder 'down' ein.",
            ["'{0}' is not a known channel name."] = "'{0}' ist kein bekannter Kanalname.",

            // SupportReportWindow
            ["Check receiver compatibility"] = "Receiver-Kompatibilität prüfen",
            ["Check whether your receiver works with the functions of this tester and help the project with the result.\n1. Choose the report type below. 'Anonymized' leaves out your MAC address and host.\n2. Click 'Run read-only operations'. Values are only read; nothing is changed on your receiver.\n3. Check the results in the list: green = ok, yellow = warning, red = error.\n4. Click 'Prepare for sending', copy the report and paste it into an e-mail yourself. Nothing is sent automatically."] = "Prüfen Sie, ob Ihr Receiver mit den Funktionen dieses Testers funktioniert, und helfen Sie dem Projekt mit dem Ergebnis.\n1. Wählen Sie unten den Berichtstyp. 'Anonymisiert' lässt Ihre MAC-Adresse und den Host weg.\n2. Klicken Sie auf 'Lese-Operationen ausführen'. Es werden nur Werte gelesen; an Ihrem Receiver wird nichts verändert.\n3. Prüfen Sie die Ergebnisse in der Liste: grün = ok, gelb = Warnung, rot = Fehler.\n4. Klicken Sie auf 'Zum Senden vorbereiten', kopieren Sie den Bericht und fügen Sie ihn selbst in eine E-Mail ein. Es wird nichts automatisch gesendet.",
            ["Report type:"] = "Berichtstyp:",
            ["Regular"] = "Normal",
            ["Anonymized (no MAC address, serial number or host)"] = "Anonymisiert (ohne MAC-Adresse, Seriennummer oder Host)",
            ["Run read-only operations"] = "Lese-Operationen ausführen",
            ["Result of the selected operation"] = "Ergebnis der gewählten Operation",
            ["Individual information (what ever you want to tell us ;-)"] = "Individuelle Angaben (alles, was Sie uns mitteilen möchten ;-)",
            ["The project may contact me by e-mail if there are questions about this report"] = "Das Projekt darf mich per E-Mail kontaktieren, wenn es Fragen zu diesem Bericht gibt",
            ["Keep me informed by e-mail about changes to the project"] = "Informieren Sie mich per E-Mail über Änderungen am Projekt",
            ["Prepare for sending"] = "Zum Senden vorbereiten",
            ["Done: {0} operations ({1} ok, {2} warnings, {3} errors)."] = "Fertig: {0} Operationen ({1} ok, {2} Warnungen, {3} Fehler).",

            // JsonPreviewWindow
            ["Check report before sending"] = "Bericht vor dem Senden prüfen",
            ["This is the report for the compatibility check of your receiver. Please read it and make sure you are happy with its content.\n1. Click 'Copy to clipboard'.\n2. Paste the report into an e-mail to {0} and send it yourself.\nNothing is sent automatically."] = "Dies ist der Bericht zur Kompatibilitätsprüfung Ihres Receivers. Bitte lesen Sie ihn durch und stellen Sie sicher, dass Sie mit dem Inhalt einverstanden sind.\n1. Klicken Sie auf 'In die Zwischenablage kopieren'.\n2. Fügen Sie den Bericht in eine E-Mail an {0} ein und senden Sie sie selbst ab.\nEs wird nichts automatisch gesendet.",
            ["Copy to clipboard"] = "In die Zwischenablage kopieren",
            ["Close"] = "Schließen",
            ["Please send this report to us by e-mail: {0}"] = "Bitte senden Sie uns diesen Bericht per E-Mail: {0}",
            ["This supports the open source project. Thank you very much!"] = "Damit unterstützen Sie das Open-Source-Projekt. Vielen Dank!",
            ["Copied to the clipboard."] = "In die Zwischenablage kopiert.",
            ["The clipboard is currently not available: {0}"] = "Die Zwischenablage ist derzeit nicht verfügbar: {0}",
            ["Please try again, or select the text and copy it with Ctrl+C."] = "Bitte versuchen Sie es erneut oder markieren Sie den Text und kopieren Sie ihn mit Strg+C.",

            // ReceiverProfileDialog
            ["Receiver profile"] = "Receiver-Profil",
            ["Profile to use for the operations of this receiver:"] = "Profil, das für die Operationen dieses Receivers verwendet werden soll:",
            ["The receiver did not report a type name. Please select the most similar type from the list and enter the type of your receiver below."] = "Der Receiver hat keinen Typnamen gemeldet. Bitte wählen Sie in der Liste den ähnlichsten Typ aus und tragen Sie unten den Typ Ihres Receivers ein.",
            ["The receiver reports the type '{0}', but it is not available in this tester. Please select the most similar type from the list."] = "Der Receiver meldet den Typ '{0}', dieser ist in diesem Tester aber nicht vorhanden. Bitte wählen Sie in der Liste den ähnlichsten Typ aus.",
            ["Type of your receiver:"] = "Typ Ihres Receivers:",
            ["<select a profile>"] = "<Profil auswählen>",
            ["Accept"] = "Übernehmen",
            ["Cancel"] = "Abbrechen" 
        };

        static Loc()
        {
            IsGerman = string.Equals(
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "de",
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGerman { get; set; }

        public static string Get(string englishText) =>
            IsGerman && German.TryGetValue(englishText, out var germanText) ? germanText : englishText;

        public static string Format(string englishFormat, params object?[] arguments) =>
            string.Format(CultureInfo.InvariantCulture, Get(englishFormat), arguments);
    }
}