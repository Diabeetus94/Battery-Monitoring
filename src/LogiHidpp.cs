// Akku-Anzeige: Logitech-Geraete direkt ueber HID++ 2.0 (LIGHTSPEED-/Unifying-/Bolt-Empfaenger und USB-Kabel),
// ohne G HUB. Nur lesende Abfragen (Name, Typ, Akku) – es werden keine Einstellungen am Geraet veraendert.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AkkuAnzeige
{
    static class LogiHidpp
    {
        const ushort VID = 0x046D;
        const byte SHORT = 0x10, LONG = 0x11, SW = 0x0A; // SW = eigene Software-ID (passt Antworten zu)
        const ushort F_ROOT = 0x0000, F_NAME = 0x0005, F_INFO = 0x0003, F_BATT_UNIFIED = 0x1004, F_BATT_STATUS = 0x1000, F_BATT_VOLT = 0x1001;

        // Geraete, die schon einmal online waren (Name/Typ bleiben sichtbar, wenn sie ausgeschaltet werden)
        class Known { public string Id, Name, Type, InputKey; public int? Pct; public bool Online; }
        static readonly Dictionary<string, Known> known = new Dictionary<string, Known>();
        // Funktionsnummern je Geraet (aendern sich nicht, solange das Geraet gekoppelt ist)
        static readonly Dictionary<string, Dictionary<ushort, byte>> featCache = new Dictionary<string, Dictionary<ushort, byte>>();

        class Endpoint { public HidInfo Short, Long; public string Key; }

        // short/long-Sammlungen derselben Schnittstelle zusammenfassen
        static List<Endpoint> Endpoints()
        {
            var res = new Dictionary<string, Endpoint>();
            foreach (var hi in Hid.Enumerate(VID))
            {
                if (hi.UsagePage != 0xFF00 || (hi.Usage != 1 && hi.Usage != 2)) continue;
                // \\?\hid#vid_046d&pid_c54d&mi_02&col01#8&2f43f743&0&0000#{...}  ->  vid_046d&pid_c54d&mi_02#8&2f43f743&0
                var m = Regex.Match(hi.Path, @"#(vid_[^#&]+&pid_[^#&]+(?:&mi_\d+)?)(?:&col\d+)?#(.+)&[0-9a-f]{4}#", RegexOptions.IgnoreCase);
                string key = m.Success ? (m.Groups[1].Value + "#" + m.Groups[2].Value).ToLowerInvariant() : hi.Path.ToLowerInvariant();
                Endpoint e;
                if (!res.TryGetValue(key, out e)) { e = new Endpoint { Key = key }; res[key] = e; }
                if (hi.Usage == 1 && hi.InLen >= 7) e.Short = hi;
                if (hi.Usage == 2 && hi.InLen >= 20) e.Long = hi;
            }
            return res.Values.Where(e => e.Long != null).ToList();
        }

        // null = kein Logitech-HID++-Geraet vorhanden
        public static List<DeviceInfo> Read()
        {
            var eps = Endpoints();
            if (eps.Count == 0) return null;
            var list = new List<DeviceInfo>();
            foreach (var ep in eps)
            {
                try { ReadEndpoint(ep, list); }
                catch (Exception ex) { Program.LogError(ex); }
            }
            // dasselbe Geraet ueber Empfaenger UND Ladekabel: der verbundene Eintrag gewinnt
            return list.GroupBy(d => d.Id).Select(g => g.OrderByDescending(d => d.Connected).First()).ToList();
        }

        static void ReadEndpoint(Endpoint ep, List<DeviceInfo> list)
        {
            using (var lng = HidDevice.Open(ep.Long))
            using (var sht = ep.Short != null ? HidDevice.Open(ep.Short) : null)
            {
                if (lng == null) return;
                var io = new Io { Long = lng, Short = sht };
                // Empfaenger: Geraete 1..6; direkt per Kabel/USB angeschlossen: 0xFF
                bool receiver = (ep.Long.Product ?? "").IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0
                             || Regex.IsMatch(ep.Key, @"pid_c5[0-9a-f]{2}", RegexOptions.IgnoreCase);
                var idxs = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new byte[] { 0xFF };
                foreach (byte idx in idxs)
                {
                    var di = ReadDevice(io, ep, idx);
                    if (di != null) list.Add(di);
                }
            }
        }

        static DeviceInfo ReadDevice(Io io, Endpoint ep, byte idx)
        {
            string slot = ep.Key + "/" + idx;
            // Ping (Root, Funktion 1): antwortet nur, wenn das Geraet eingeschaltet und verbunden ist
            var ping = io.Request(idx, 0x00, 1, new byte[] { 0, 0, 0xAA }, 400);
            Known k;
            known.TryGetValue(slot, out k);
            if (ping == null || ping.Length < 7 || ping[4] < 2)
            {
                // ausgeschaltet / Ruhemodus: zuletzt bekanntes Geraet als "getrennt" melden
                if (k == null) return null;
                k.Online = false;
                return Make(k, false, null, false);
            }

            // wieder eingeschaltet: Name und Funktionsnummern neu lesen (der Platz im Empfaenger
            // kann inzwischen mit einem anderen Geraet gekoppelt sein)
            if (k != null && !k.Online) { k = null; featCache.Remove(slot); }
            var feats = Feats(slot);
            if (k == null)
            {
                k = new Known();
                var vp = Regex.Match(ep.Key, @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})");
                if (vp.Success) k.InputKey = (vp.Groups[1].Value + ":" + vp.Groups[2].Value).ToUpperInvariant(); // fuer "unbenutzt -> grau"
                k.Name = DeviceName(io, idx, feats) ?? "Logitech";
                k.Type = DeviceType(io, idx, feats);
                k.Id = "logi:" + k.Name.Split(' ')[0].ToUpperInvariant(); // gleiche ID wie bei G HUB / LGSTray (z.B. logi:G502)
                known[slot] = k;
            }

            k.Online = true;
            bool charging;
            int? pct = Battery(io, idx, feats, out charging);
            if (!pct.HasValue && k.Pct == null && !charging) return null; // kein Akku (z.B. kabelgebundene Tastatur)
            if (pct.HasValue) k.Pct = pct;
            return Make(k, true, pct, charging);
        }

        static DeviceInfo Make(Known k, bool online, int? pct, bool charging)
        {
            return new DeviceInfo
            {
                Id = k.Id,
                Name = k.Name,
                Source = "Logitech (HID++)",
                Percent = pct,
                Charging = charging,
                Connected = online,
                DefaultIcon = k.Type,
                InputKey = k.InputKey
            };
        }

        static Dictionary<ushort, byte> Feats(string slot)
        {
            Dictionary<ushort, byte> f;
            if (!featCache.TryGetValue(slot, out f)) { f = new Dictionary<ushort, byte>(); featCache[slot] = f; }
            return f;
        }

        // Funktionsnummer eines Features (0 = nicht vorhanden)
        static byte Feature(Io io, byte idx, Dictionary<ushort, byte> cache, ushort id)
        {
            byte fi;
            if (cache.TryGetValue(id, out fi)) return fi;
            var r = io.Request(idx, 0x00, 0, new byte[] { (byte)(id >> 8), (byte)id }, 400);
            fi = r != null && r.Length > 4 ? r[4] : (byte)0;
            if (r != null) cache[id] = fi; // nur echte Antworten merken
            return fi;
        }

        static string DeviceName(Io io, byte idx, Dictionary<ushort, byte> f)
        {
            byte fi = Feature(io, idx, f, F_NAME);
            if (fi == 0) return null;
            var r = io.Request(idx, fi, 0, null, 400);
            if (r == null) return null;
            int len = r[4];
            var sb = new StringBuilder();
            while (sb.Length < len)
            {
                var c = io.Request(idx, fi, 1, new byte[] { (byte)sb.Length }, 400);
                if (c == null) break;
                int before = sb.Length;
                for (int i = 4; i < c.Length && sb.Length < len; i++) { if (c[i] == 0) { len = sb.Length; break; } sb.Append((char)c[i]); }
                if (sb.Length == before) break;
            }
            string n = sb.ToString().Trim();
            return n.Length == 0 ? null : n;
        }

        static string DeviceType(Io io, byte idx, Dictionary<ushort, byte> f)
        {
            byte fi = Feature(io, idx, f, F_NAME);
            var r = fi == 0 ? null : io.Request(idx, fi, 2, null, 400);
            int t = r == null ? -1 : r[4];
            switch (t)
            {
                case 0: case 2: return "Tastatur";               // Tastatur, Ziffernblock
                case 3: case 4: case 5: return "Maus";           // Maus, Touchpad, Trackball
                case 8: return "Kopfhörer";                      // Headset
                default: return "Bluetooth";
            }
        }

        static int? Battery(Io io, byte idx, Dictionary<ushort, byte> f, out bool charging)
        {
            charging = false;
            byte fi = Feature(io, idx, f, F_BATT_UNIFIED);
            if (fi != 0)
            {
                // getStatus: [0] Ladestand %, [1] Stufe, [2] Ladezustand (0 entlaedt, 1/2 laedt, 3 voll), [3] externe Stromversorgung
                var r = io.Request(idx, fi, 1, null, 400);
                if (r != null)
                {
                    charging = r[6] == 1 || r[6] == 2;
                    int soc = r[4];
                    if (soc > 0 && soc <= 100) return soc;
                    // manche Geraete melden nur Stufen: 8 voll, 4 gut, 2 niedrig, 1 kritisch
                    int lv = r[5];
                    if (lv != 0) return (lv & 8) != 0 ? 100 : (lv & 4) != 0 ? 50 : (lv & 2) != 0 ? 20 : 5;
                    return null;
                }
            }
            fi = Feature(io, idx, f, F_BATT_STATUS);
            if (fi != 0)
            {
                // getBatteryLevelStatus: [0] Ladestand %, [1] naechste Stufe, [2] Status (0 entlaedt, 1-2/4 laedt, 3 voll)
                var r = io.Request(idx, fi, 0, null, 400);
                if (r != null)
                {
                    charging = r[6] == 1 || r[6] == 2 || r[6] == 4;
                    if (r[4] > 0 && r[4] <= 100) return r[4];
                    return null;
                }
            }
            fi = Feature(io, idx, f, F_BATT_VOLT);
            if (fi != 0)
            {
                // getBatteryInfo: [0..1] Spannung in mV, [2] Flags (Bit 7: laedt)
                var r = io.Request(idx, fi, 0, null, 400);
                if (r != null)
                {
                    int mv = (r[4] << 8) | r[5];
                    charging = (r[6] & 0x80) != 0;
                    if (mv > 0) return VoltToPercent(mv);
                }
            }
            return null;
        }

        // Entladekurve eines typischen 1-Zellen-Li-Ion-Akkus (wie Solaar)
        static readonly int[,] curve =
        {
            { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 },
            { 3778, 40 }, { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 }
        };

        static int VoltToPercent(int mv)
        {
            if (mv >= curve[0, 0]) return 100;
            for (int i = 1; i < curve.GetLength(0); i++)
                if (mv >= curve[i, 0])
                {
                    double t = (double)(mv - curve[i, 0]) / (curve[i - 1, 0] - curve[i, 0]);
                    return (int)Math.Round(curve[i, 1] + t * (curve[i - 1, 1] - curve[i, 1]));
                }
            return 0;
        }

        // Anfrage/Antwort ueber die Kurz- und Langberichte
        class Io
        {
            public HidDevice Long, Short;

            // Antwort (Langbericht, Nutzdaten ab Index 4) oder null bei Fehler/Timeout/Geraet nicht verbunden
            public byte[] Request(byte idx, byte feat, int func, byte[] args, int timeoutMs)
            {
                var req = new byte[20];
                req[0] = LONG; req[1] = idx; req[2] = feat; req[3] = (byte)((func << 4) | SW);
                if (args != null) Array.Copy(args, 0, req, 4, Math.Min(args.Length, 16));
                bool sent = Long.Write(req, 300);
                if (!sent && Short != null)
                {
                    var s = new byte[7];
                    Array.Copy(req, s, 7);
                    s[0] = SHORT;
                    sent = Short.Write(s, 300);
                }
                if (!sent) return null;

                var end = Environment.TickCount + timeoutMs;
                while (true)
                {
                    int left = end - Environment.TickCount;
                    if (left <= 0) return null;
                    // Fehler kommen als Kurzbericht (HID++ 1.0: 0x8F) – kurz pruefen, dann auf die Langantwort warten
                    if (Short != null)
                    {
                        var e = Short.Read(Math.Min(left, 25));
                        if (e != null && e.Length >= 7 && e[1] == idx && e[2] == 0x8F && e[3] == feat && e[4] == ((func << 4) | SW)) return null;
                        left = end - Environment.TickCount;
                        if (left <= 0) return null;
                    }
                    var r = Long.Read(Math.Min(left, Short != null ? 60 : left));
                    if (r == null || r.Length < 5 || r[1] != idx) continue;
                    if (r[2] == 0xFF && r[3] == feat && r[4] == ((func << 4) | SW)) return null;      // HID++ 2.0 Fehler
                    if (r[2] == feat && r[3] == ((func << 4) | SW)) return r;                         // passende Antwort
                }
            }
        }
    }
}
