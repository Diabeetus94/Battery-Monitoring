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
            { "GRENZ_INFO", "The alarm applies to every device shown on at least one monitor – as its own warning window with a warning sound that also appears with \"Do not disturb\", muted notifications and in game mode.\n\nLogitech devices come directly from G HUB (LGSTray only as fallback). Bluetooth devices appear as soon as Windows knows their battery level.\n\nChanges are shown immediately as a preview. They are only saved with \"Apply\" or \"OK\" – \"Cancel\" discards them." },

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
            { "Windows-Sounds im Menü oder eigene Datei (WAV, MP3, WMA, M4A, AAC). Lautstärke gilt für jedes Ausgabegerät.", "Windows sounds in the menu or your own file (WAV, MP3, WMA, M4A, AAC). Volume applies to every output device." },
            { "Standard-Warnton (Akku-Anzeige)", "Default warning tone (Battery Display)" },
            { "Eigene Datei: {0}", "Own file: {0}" },
            { " (nicht gefunden)", " (not found)" },
            { "OPT_INFO", "Minimized: battery tiles and alarm keep running in the background. Reopen this window via the desktop shortcut \"Akku-Anzeige\" or by double-clicking the icon in the notification area.\n\nThe battery tiles on the taskbar always match the taskbar color automatically." },

            // Info-Fenster
            { "Info", "About" },
            { "Über Akku-Anzeige", "About Battery Display" },
            { "Akkustände von Maus, Tastatur, Kopfhörern und weiteren drahtlosen Geräten direkt auf der Taskleiste.", "Battery levels of mouse, keyboard, headphones and other wireless devices right on the taskbar." },
            { "Version {0}", "Version {0}" },
            { "Hersteller: {0}", "Publisher: {0}" },
            { "Datenquellen", "Data sources" },
            { "Erkannte Hersteller-Software", "Detected vendor software" },
            { "Windows-Bluetooth (eingebaut)", "Windows Bluetooth (built in)" },
            { "Akkustand von Bluetooth-Geräten (Kopfhörer, Lautsprecher, Handys …)", "Battery level of Bluetooth devices (headphones, speakers, phones …)" },
            { "unterstützt", "supported" }, { "läuft", "running" }, { "installiert", "installed" }, { "nicht installiert", "not installed" },
            { "Nach Updates suchen", "Check for updates" },
            { "Website öffnen", "Open website" },
            { "Schließen", "Close" },
            { "Suche nach Updates …", "Checking for updates …" },
            { "Du hast die neueste Version ({0}).", "You have the latest version ({0})." },
            { "Update verfügbar: Version {0} (installiert: {1}).", "Update available: version {0} (installed: {1})." },
            { "Jetzt herunterladen?", "Download now?" },
            { "Update-Suche fehlgeschlagen: {0}", "Update check failed: {0}" },
            { "Es ist noch keine Update-Adresse (GitHub) eingerichtet.", "No update address (GitHub) has been set up yet." },
            { "Update verfügbar", "Update available" },
            { "verknüpft", "linked" }, { "nicht verknüpft", "not linked" }, { "nicht kompatibel", "not compatible" },
            { "Datenquellen (verknüpfte Treiber-Software)", "Data sources (linked driver software)" },
            { "Nicht kompatible oder nicht installierte Programme erscheinen hier nicht.", "Incompatible or not installed programs are not listed here." },
            { "{0} wurde gefunden und ist mit der Akku-Anzeige kompatibel.\n\n{1}\n\nJetzt verknüpfen?", "{0} was found and is compatible with Battery Display.\n\n{1}\n\nLink it now?" },
            { "Das Projekt ist privat oder hat noch keine Veröffentlichung – die automatische Update-Suche ist erst möglich, wenn es öffentlich ist.", "The project is private or has no release yet – automatic update checks only work once it is public." },
            { "Download", "Download" }
        };
    }
}
