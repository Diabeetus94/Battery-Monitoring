# Changelog – Akku-Anzeige

## 1.3.1 Alpha – 05.11.2026
- Neu: Geräte direkt in der Taskleiste tauschen – Klick auf eine Kachel öffnet darüber ein Fenster mit allen Geräten; die Auswahl wird sofort gespeichert (Einstellungen → Taskleiste, abschaltbar)
- Neu: Windows-Mitteilung, wenn ein Update verfügbar ist (Einstellungen → Updates, abschaltbar)
- Neu: automatische Updates – laden, prüfen und installieren sich selbst (Einstellungen → Updates, standardmäßig aus)
- Rechtsklick-Menü im Design des Programms (hell/dunkel)

## 1.3.0 Alpha – 05.11.2026
- Symbol im Infobereich der Taskleiste (unten rechts) ist jetzt dasselbe wie das Programm-Symbol

## 1.2.0.4 Alpha – 05.11.2026
- Neu: „Bug melden“ unter Einstellungen → Info – führt direkt zu GitHub: „Issue erstellen“ (Fehler melden, Ideen) oder „Pull Request erstellen“ (Code-Änderung vorschlagen)
- © 2026 Bobohatt & Diabeetus
- Behoben: Lücken in den Rahmen (z. B. bei „Alarm-Ton“) – Rahmen passen jetzt zum hellen und dunklen Design
- Behoben: Bildreste beim Scrollen im Reiter „Einstellungen“
- Behoben: Das Mausrad verstellte beim Scrollen Auswahlfelder und Regler (z. B. den Alarm-Sound) – es scrollt jetzt die Seite
- Behoben: weißes Kästchen rechts oben in der Geräteliste
- Behoben: helle Bildlaufleisten in Changelog-, Update- und Bug-Fenster
- Behoben: „&“ wurde in Beschriftungen nicht angezeigt

## 1.2.0.3 Alpha – 05.11.2026
Mit Beiträgen von garpu-ng (Pull Requests #1 und #2) – vielen Dank!
- Neue eingebaute Gerätequellen ohne Hersteller-Software: Logitech direkt über den Empfänger (HID++), HyperX Cloud III S Wireless, PlayStation DualSense, AirPods/Beats (Bluetooth LE)
- „Treiber & Geräte suchen“ unter Datenquellen: findet nachträglich installierte Software und angeschlossene Geräte
- Eigene Farben für die Kacheln mit der Windows-Farbauswahl
- Unterstützung für eine automatisch ausgeblendete Taskleiste – die Kacheln fahren mit
- Behoben: Beim Tippen im Feld „Anzeigename“ ging nach jedem Buchstaben der Fokus verloren
- Behoben: Maximierte Fenster wurden bei ausgeblendeter Taskleiste als Vollbild-Spiel erkannt
- Behoben: Kacheln blinkten beim Klick auf die Taskleiste kurz weg
- Neue Installationen starten ohne vorgegebene Beispielgeräte und zeigen die eigenen Geräte automatisch
- Weniger Prozessorlast im Leerlauf
- Changelog-Fenster: nur noch eine Leerzeile zwischen den Versionen

## 1.2.0.2 Alpha – 05.11.2026
- Updates werden jetzt direkt in der App heruntergeladen und installiert – ohne Browser
- Vor der Installation wird geprüft, dass das Update mit dem Zertifikat der Akku-Anzeige signiert und unverändert ist
- Neues Update-Fenster mit Versionsnummer und den Änderungen der neuen Version
- Changelog direkt in der App (Einstellungen → Info → Changelog)
- Setup: stiller Update-Modus, übernimmt die bisherigen Optionen und startet die App danach neu

## 1.2.0.1 Alpha – 05.11.2026
- Info-Bereich in den Reiter „Einstellungen“ verschoben (Version, Hersteller, Update-Suche)
- Reiter „Einstellungen“ scrollbar – Platz für weitere Bereiche, dunkle Bildlaufleiste
- Update-Suche über eine Update-Info, GitHub-Veröffentlichungen als Ersatz; vierstellige Versionsnummern
- Ausgegraute Auswahlfelder im dunklen Design mit weißer Schrift

## 1.2.0 Alpha – 05.11.2026
- Erste Alpha-Version
- Akku-Kacheln auf der Taskleiste, je Monitor eigene Geräte, Reihenfolge und Breite – auch für ausgeschaltete Monitore
- Logitech G HUB (Akkustand, Laden, Aktiv/Standby/Aus) und Windows-Bluetooth
- Getrennte Geräte und Standby grau mit letztem Akkustand
- Eigenes Warnfenster mit Warnton – auch bei „Nicht stören“ und im Spielmodus; Lautstärke und Sound wählbar
- Farbbereiche Rot/Orange/Grün einstellbar
- Heller/dunkler Modus automatisch, Deutsch/Englisch
- Sichtbar über Startmenü und Taskleisten-Menüs (UIAccess)
- Setup mit Erkennung installierter, kompatibler Treiber-Software
