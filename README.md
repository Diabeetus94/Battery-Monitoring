# Battery-Monitoring (Akku-Anzeige)

> **Alpha-Version** – noch in der Testphase.

Zeigt die Akkustände drahtloser Geräte (Maus, Tastatur, Kopfhörer …) als Kacheln direkt auf der Windows-Taskleiste.

**Hersteller:** Diabeetus · © 2026 · Powered by Claude.ai

## Funktionen
- Kacheln links auf der Taskleiste, je Monitor einzeln einstellbar (Geräte, Reihenfolge, Breite, an/aus) – auch für ausgeschaltete Monitore
- Farbbereiche Rot / Orange / Grün frei einstellbar
- Getrennte Geräte bzw. Geräte im Standby werden grau mit letztem Akkustand angezeigt
- Eigenes Alarmfenster mit Warnton bei niedrigem Akku – auch bei „Nicht stören“ und im Spielmodus
- Alarm-Lautstärke und eigener oder Windows-Sound wählbar
- Heller / dunkler Modus automatisch, Deutsch / Englisch
- Bleibt über Startmenü und Taskleisten-Menüs sichtbar (UIAccess)

## Datenquellen
| Quelle | Status |
|---|---|
| Logitech G HUB | kompatibel (Akku + Aktiv/Standby) |
| Windows-Bluetooth | kompatibel |
| LGSTray | kompatibel (Ersatzquelle) |
| Razer, Corsair, SteelSeries, HyperX, ASUS | werden erkannt, Akkustände noch nicht |

## Installation
`AkkuAnzeige-Setup-<version>.exe` aus den Releases starten. Das Setup erkennt installierte, kompatible Treiber-Software und fragt, ob sie verknüpft werden soll.

## Bauen
Windows mit .NET Framework 4.8: `src\build.ps1` (kompiliert mit dem eingebauten `csc.exe`, signiert und baut das Setup). Zum Signieren wird ein lokales Code-Signatur-Zertifikat „Akku-Anzeige Code Signing (lokal)“ benötigt.

## Systemvoraussetzungen
Windows 10/11, .NET Framework 4.8, für Logitech-Geräte: Logitech G HUB.
