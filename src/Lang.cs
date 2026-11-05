// Sprachen: Deutsch (Grundtext im Code) und Englisch.
// Neue Sprache: weiteres Woerterbuch anlegen und in Languages eintragen.
using System;
using System.Collections.Generic;

namespace AkkuAnzeige
{
    public static class L
    {
        public static string Current = "de";

        // Code -> Anzeigename (in der jeweiligen Sprache selbst)
        public static readonly KeyValuePair<string, string>[] Languages =
        {
            new KeyValuePair<string, string>("de", "Deutsch"),
            new KeyValuePair<string, string>("en", "English")
        };

        public static string T(string de)
        {
            if (Current == "en") { string s; if (En.TryGetValue(de, out s)) return s; }
            return de;
        }

        public static string F(string de, params object[] args) { return string.Format(T(de), args); }

        // lange Texte: ueber einen Schluessel statt ueber den ganzen deutschen Text
        public static string K(string key, string de)
        {
            if (Current == "en") { string s; if (En.TryGetValue(key, out s)) return s; }
            return de;
        }

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // Tray / allgemein
            { "Einstellungen …", "Settings …" },
            { "Jetzt aktualisieren", "Refresh now" },
            { "Beenden", "Exit" },
            { "Info …", "About …" },

            // Symbole
            { "Maus", "Mouse" }, { "Tastatur", "Keyboard" }, { "Kopfhörer", "Headphones" }, { "Lautsprecher", "Speaker" },
            { "Controller", "Controller" }, { "Handy", "Phone" }, { "Bluetooth", "Bluetooth" },

            // Alarm
            { "Akku fast leer: {0}", "Battery low: {0}" },
            { "{0} hat nur noch {1} % Akku – bitte aufladen!", "{0} has only {1} % battery left – please charge!" },
            { "{0}: noch {1} %", "{0}: {1} % left" },
            { "Bitte aufladen!", "Please charge!" },
            { "TEST – {0} hat nur noch {1} % Akku – bitte aufladen!", "TEST – {0} has only {1} % battery left – please charge!" },

            // Fenster
            { "Akku-Anzeige – Einstellungen", "Battery Display – Settings" },
            { "Monitore identifizieren", "Identify monitors" },
            { "Übernehmen", "Apply" },
            { "Abbrechen", "Cancel" },

            // Monitor-Reiter
            { " (aus)", " (off)" },
            { "Hauptbildschirm", "main display" },
            { "deaktiviert – die Leiste erscheint automatisch, sobald er eingeschaltet wird", "disabled – the bar appears automatically once it is switched on" },
            { "nicht verbunden – die Leiste erscheint automatisch, sobald er angeschlossen wird", "not connected – the bar appears automatically once it is plugged in" },
            { "Akku-Leiste auf diesem Monitor anzeigen", "Show battery bar on this monitor" },
            { "Breite der Kacheln: {0} %", "Tile width: {0} %" },
            { "Die Höhe passt sich immer der Taskleiste an – der Regler ändert nur die Breite.", "The height always matches the taskbar – the slider only changes the width." },
            { "Geräte auf diesem Monitor", "Devices on this monitor" },
            { "Angehakte Geräte werden als Kachel angezeigt – in der Reihenfolge der Liste (Zeilen mit der Maus ziehen).", "Checked devices are shown as tiles – in list order (drag rows with the mouse)." },
            { "Anzeigename", "Display name" }, { "Gerät", "Device" }, { "Verbindung", "Connection" }, { "Akku", "Battery" },
            { "▲  Nach oben", "▲  Move up" }, { "▼  Nach unten", "▼  Move down" }, { "Geräte neu suchen", "Search devices" },
            { "Anzeigename:", "Display name:" }, { "Symbol:", "Icon:" },
            { "getrennt", "disconnected" }, { "Ruhe/aus", "standby/off" }, { " (lädt)", " (charging)" },
            { "(nicht gefunden)", "(not found)" }, { "(G HUB?)", "(G HUB?)" },

            // Grenzwerte
            { "Grenzwerte", "Thresholds" },
            { "Alarm (gilt für alle Monitore)", "Alarm (applies to all monitors)" },
            { "Test-Alarm", "Test alarm" },
            { "Alarm, wenn ein Gerät {0} % oder weniger hat", "Alarm when a device has {0} % or less" },
            { "Farbbereiche (gilt für alle Monitore)", "Color ranges (applies to all monitors)" },
            { "Rot: 0 – {0} %", "Red: 0 – {0} %" }, { "Orange: {0} – {1} %", "Orange: {0} – {1} %" }, { "Grün: {0} – 100 %", "Green: {0} – 100 %" },
            { "Die beiden Griffe auf der Linie verschieben: links Rot, Mitte Orange, rechts Grün.", "Drag the two handles on the line: left red, middle orange, right green." },
            { "Getrennte Geräte und Geräte im Ruhemodus grau anzeigen (letzter Akkustand bleibt sichtbar)", "Show disconnected and sleeping devices in gray (last battery level stays visible)" },
            { "Maus/Tastatur auch grau anzeigen, wenn sie unbenutzt sind seit", "Also show mouse/keyboard in gray when unused for" },
            { "Minuten", "minutes" },
            { "GRENZ_INFO", "The alarm applies to every device shown on at least one monitor – as its own warning window with a warning sound that also appears with \"Do not disturb\", muted notifications and in game mode.\n\nLogitech devices come from G HUB – without G HUB from LGSTray or directly from the receiver (HID++). Bluetooth devices appear as soon as Windows knows their battery level; AirPods/Beats via their Bluetooth LE broadcasts.\n\nChanges are shown immediately as a preview. They are only saved with \"Apply\" or \"OK\" – \"Cancel\" discards them." },

            // Einstellungen
            { "Einstellungen", "Settings" },
            { "Programm", "Program" },
            { "Mit Windows starten", "Start with Windows" },
            { "Minimiert starten (nur die Kacheln, ohne dieses Fenster)", "Start minimized (tiles only, without this window)" },
            { "Beim Schließen des Fensters (X):", "When closing the window (X):" },
            { "Minimieren (läuft im Hintergrund weiter)", "Minimize (keeps running in background)" },
            { "Programm komplett beenden", "Exit program completely" },
            { "Darstellung", "Appearance" },
            { "Fensterdesign automatisch an Windows anpassen (heller / dunkler Modus)", "Match window design to Windows automatically (light / dark mode)" },
            { "Design, wenn nicht automatisch:", "Design if not automatic:" },
            { "Hell", "Light" }, { "Dunkel", "Dark" },
            { "Sprache:", "Language:" },
            { "Die Sprache wird nach \"Übernehmen\" sofort umgestellt.", "The language changes immediately after \"Apply\"." },
            { "Alarm-Ton", "Alarm sound" },
            { "Lautstärke des Alarms: {0} %", "Alarm volume: {0} %" },
            { "▶  Ton testen", "▶  Test sound" },
            { "Sound:", "Sound:" },
            { "Durchsuchen …", "Browse …" },
            { "Standard", "Default" },
            { "Alarm-Sounddatei wählen", "Choose alarm sound file" },
            { "Audiodateien", "Audio files" }, { "Alle Dateien", "All files" },
            { "Windows-Sound im Menü oder eigene Datei (WAV, MP3, WMA, M4A, AAC).", "Windows sound from the menu or your own file (WAV, MP3, WMA, M4A, AAC)." },
            { "Standard-Warnton (Akku-Anzeige)", "Default warning tone (Battery Display)" },
            { "Eigene Datei: {0}", "Own file: {0}" },
            { " (nicht gefunden)", " (not found)" },
            { "OPT_INFO", "Minimized: battery tiles and alarm keep running in the background. Reopen this window via the desktop shortcut \"Akku-Anzeige\" or by double-clicking the icon in the notification area.\n\nThe battery tiles on the taskbar always match the taskbar color automatically." },

            // Info-Fenster
            { "Info", "About" },
            { "Version {0}", "Version {0}" },
            { "Hersteller: {0}", "Publisher: {0}" },
            { "Datenquellen", "Data sources" },
            { "läuft", "running" }, { "installiert", "installed" },
            { "Nach Updates suchen", "Check for updates" },
            { "Website öffnen", "Open website" },
            { "Schließen", "Close" },
            { "Suche nach Updates …", "Checking for updates …" },
            { "Du hast die neueste Version ({0}).", "You have the latest version ({0})." },
            { "Update verfügbar: Version {0} (installiert: {1}).", "Update available: version {0} (installed: {1})." },
            { "Update-Suche fehlgeschlagen: {0}", "Update check failed: {0}" },
            { "Es ist noch keine Update-Adresse (GitHub) eingerichtet.", "No update address (GitHub) has been set up yet." },
            { "Update verfügbar", "Update available" },
            { "Changelog", "Changelog" },
            { "Changelog wird geladen …", "Loading changelog …" },
            { "Changelog konnte nicht geladen werden.", "The changelog could not be loaded." },
            { "Auf GitHub ansehen", "View on GitHub" },
            { "Version {0} ist verfügbar", "Version {0} is available" },
            { "Installiert: {0}", "Installed: {0}" },
            { "Was ist neu:", "What's new:" },
            { "Update installieren", "Install update" },
            { "Später", "Later" },
            { "Download-Seite öffnen", "Open download page" },
            { "Für diese Version ist kein Changelog vorhanden.", "There is no changelog for this version." },
            { "Lade Update herunter …", "Downloading update …" },
            { "Lade Update herunter … {0} %", "Downloading update … {0} %" },
            { "Download fehlgeschlagen: {0}", "Download failed: {0}" },
            { "Prüfe Signatur …", "Checking signature …" },
            { "Update abgebrochen – die Datei ist nicht vertrauenswürdig:", "Update cancelled – the file is not trustworthy:" },
            { "Prüfsumme stimmt nicht.", "Checksum does not match." },
            { "Signatur ungültig (0x{0:X8}).", "Invalid signature (0x{0:X8})." },
            { "Unbekannter Herausgeber.", "Unknown publisher." },
            { "Keine Signatur gefunden.", "No signature found." },
            { "Installiere Update … (bitte die Windows-Abfrage bestätigen)", "Installing update … (please confirm the Windows prompt)" },
            { "Installation abgebrochen.", "Installation cancelled." },
            { "Bug melden", "Report a bug" },
            { "Wie möchtest du etwas melden?", "How would you like to report something?" },
            { "Beide Wege öffnen GitHub in deinem Browser. Dort füllst du alles selbst aus (GitHub-Konto nötig).", "Both options open GitHub in your browser. You fill in everything there yourself (GitHub account needed)." },
            { "Issue erstellen", "Create issue" },
            { "Fehler melden oder eine Idee vorschlagen", "Report a bug or suggest an idea" },
            { "Pull Request erstellen", "Create pull request" },
            { "Eine Code-Änderung vorschlagen", "Suggest a code change" },
            { "nicht kompatibel", "not compatible" },
            { "Nicht kompatible oder nicht installierte Programme erscheinen hier nicht.", "Incompatible or not installed programs are not listed here." },
            { "Treiber && Geräte suchen", "Scan drivers && devices" },
            { "Suche nach Treibern und Geräten …", "Scanning for drivers and devices …" },
            { "Suche fehlgeschlagen.", "Scan failed." },
            { "neu", "new" },
            // Farben
            { "Farben:", "Colors:" }, { "Rot", "Red" }, { "Grün", "Green" }, { "Laden", "Charging" },
            { "Standardfarben", "Default colors" }, { "Standardfarbe", "Default color" },
            { "Farbe für \"{0}\" wählen", "Choose color for \"{0}\"" },
            { "{0} wurde gefunden und ist mit der Akku-Anzeige kompatibel.\n\n{1}\n\nJetzt verknüpfen?", "{0} was found and is compatible with Battery Display.\n\n{1}\n\nLink it now?" },
            { "Das Projekt ist privat oder hat noch keine Veröffentlichung – die automatische Update-Suche ist erst möglich, wenn es öffentlich ist.", "The project is private or has no release yet – automatic update checks only work once it is public." },
            // Geraet in der Taskleiste tauschen
            { "Gerät tauschen", "Swap device" },
            { "Kachel {0} – Gerät wählen", "Tile {0} – choose device" },
            { "Ein Klick übernimmt das Gerät sofort.", "One click applies the device immediately." },
            { "Keine Geräte gefunden.", "No devices found." },
            { "Alle Einstellungen …", "All settings …" },
            { "↔ Kachel {0}", "↔ tile {0}" },
            { "lädt", "charging" }, { "verbunden", "connected" }, { "nicht gefunden", "not found" },
            { "Taskleiste", "Taskbar" },
            { "Geräte direkt in der Taskleiste tauschen (Klick auf eine Kachel)", "Swap devices right on the taskbar (click a tile)" },
            { "Die Auswahl wird sofort gespeichert. Ausgeschaltet aktualisiert ein Klick nur die Akkustände.", "The choice is saved immediately. When off, a click only refreshes the battery levels." },
            // Updates im Hintergrund
            { "Updates", "Updates" },
            { "Windows-Mitteilung, wenn ein Update verfügbar ist", "Windows notification when an update is available" },
            { "Updates automatisch herunterladen und installieren", "Download and install updates automatically" },
            { "Vor der Installation fragt Windows einmal nach der Berechtigung. Während eines Vollbild-Spiels wird nicht installiert.", "Windows asks for permission once before installing. Nothing is installed during a fullscreen game." },
            { "Akku-Anzeige {0} ist verfügbar (installiert: {1}). Hier klicken, um das Update zu installieren.", "Battery Display {0} is available (installed: {1}). Click here to install the update." },
            { "Update wird installiert", "Installing update" },
            { "Akku-Anzeige {0} wird heruntergeladen und installiert.", "Battery Display {0} is being downloaded and installed." },
            { "Update fehlgeschlagen", "Update failed" },
            { "Update nicht installiert", "Update not installed" },
            { "Hier klicken, um es von Hand zu installieren.", "Click here to install it manually." },
            // Datenquellen: nur Gefundenes
            { "Datenquellen (auf diesem PC gefunden)", "Data sources (found on this PC)" },
            { "Eingebaut – liest das Gerät direkt, ohne Zusatzprogramm.", "Built in – reads the device directly, no extra software." },
            { "Läuft auf diesem PC.", "Running on this PC." },
            { "Auf diesem PC installiert.", "Installed on this PC." },
            { "Auf diesem PC wurde keine kompatible Quelle gefunden.", "No compatible source was found on this PC." },
            { "Neu gefunden: {0}", "Newly found: {0}" },
            { "Nichts Neues gefunden.", "Nothing new found." },
            { "Nur was auf diesem PC gefunden wurde – Maus über einen Eintrag für Details.", "Only what was found on this PC – hover an entry for details." },
            { "Neue Software zum Verknüpfen anhaken und \"Übernehmen\".", "Check new software to link it and click \"Apply\"." },
            // Status je Datenquelle
            { "installiert – aber nicht kompatibel", "installed – but not compatible" },
            { "Gelieferte Geräte anzeigen", "Show delivered devices" },
            { "Geräte", "Devices" },
            { "kompatibel · ausgeschaltet", "compatible · turned off" },
            { "kompatibel · wird nach \"Übernehmen\" genutzt", "compatible · used after \"Apply\"" },
            { "kompatibel · wird geprüft …", "compatible · checking …" },
            { "kompatibel · liefert 1 Gerät", "compatible · delivers 1 device" },
            { "kompatibel · liefert {0} Geräte", "compatible · delivers {0} devices" },
            { "kompatibel · zurzeit kein Gerät eingeschaltet", "compatible · no device switched on right now" },
            { "kompatibel · Ersatz, wird gerade nicht gebraucht", "compatible · backup, not needed right now" },
            { "kompatibel · Programm läuft nicht", "compatible · program is not running" },
            { "kompatibel · antwortet gerade nicht", "compatible · not responding right now" }
        };
    }
}
