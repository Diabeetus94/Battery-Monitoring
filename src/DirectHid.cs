// Akku-Anzeige: weitere Geraete direkt ueber HID (ohne Hersteller-Software), nur lesende Abfragen.
//  - HyperX Cloud III S Wireless (Dongle 03F0:06BE / 03F0:02CC)
//  - Sony DualSense / DualSense Edge (USB und Bluetooth)
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkkuAnzeige
{
    static class HyperX
    {
        public const ushort VID = 0x03F0;
        public static readonly ushort[] Pids = { 0x06BE, 0x02CC }; // Cloud III S Wireless (zwei Revisionen)

        // Anfrage: 64-Byte-Ausgabebericht 0C 02 03 01 00 <Befehl>; Antwort 0C ... <Befehl> <Wert> (Wert an Index 6)
        const byte OP_LINK = 0x02, OP_BATTERY = 0x06, OP_CHARGING = 0x48;

        public static List<DeviceInfo> Read()
        {
            var list = new List<DeviceInfo>();
            foreach (var hi in Hid.Enumerate(VID, Pids).Where(h => h.UsagePage == 0x01C0 && h.Usage == 0x0001))
            {
                try
                {
                    using (var d = HidDevice.Open(hi))
                    {
                        if (d == null) continue;
                        int link = Query(d, OP_LINK);
                        if (link < 0) continue; // Dongle antwortet nicht
                        var di = new DeviceInfo
                        {
                            Id = "hyperx:" + hi.Pid.ToString("X4"),
                            Name = string.IsNullOrEmpty(hi.Product) ? "HyperX Cloud III S" : hi.Product.Replace(" Wireless", ""),
                            Source = "HyperX (HID)",
                            DefaultIcon = "Kopfhörer"
                        };
                        int pct = link == 2 ? Query(d, OP_BATTERY) : -1;
                        di.Connected = link == 2 && pct >= 0 && pct <= 100; // 0xFF = Headset aus
                        if (di.Connected)
                        {
                            di.Percent = pct;
                            int chg = Query(d, OP_CHARGING);
                            di.Charging = chg == 1; // 0 entlaedt, 1 laedt, 2 voll
                        }
                        list.Add(di);
                    }
                }
                catch (Exception ex) { Program.LogError(ex); }
            }
            return list;
        }

        // Wert (Index 6) oder -1; andere Berichte (z.B. von NGENUITY angestossene) werden uebersprungen
        static int Query(HidDevice d, byte op)
        {
            if (!d.Write(new byte[] { 0x0C, 0x02, 0x03, 0x01, 0x00, op }, 500)) return -1;
            var end = Environment.TickCount + 1000;
            while (Environment.TickCount - end < 0)
            {
                var r = d.Read(Math.Max(1, end - Environment.TickCount));
                if (r == null) return -1;
                if (r.Length > 6 && r[0] == 0x0C && r[1] == 0x02 && r[2] == 0x03 && r[5] == op) return r[6];
            }
            return -1;
        }
    }

    static class DualSense
    {
        public const ushort VID = 0x054C;
        public static readonly ushort[] Pids = { 0x0CE6, 0x0DF2 }; // DualSense, DualSense Edge

        // Nur passiv mitlesen: Ueber Bluetooth sendet der Controller den vollen Bericht 0x31 (mit Akku) erst,
        // nachdem ihn ein Programm (z.B. Steam) umgeschaltet hat. Selbst umschalten wuerde DirectInput-Spiele
        // stoeren und laesst sich ohne Aus-/Einschalten des Controllers nicht rueckgaengig machen.
        public static List<DeviceInfo> Read()
        {
            var list = new List<DeviceInfo>();
            var seen = new HashSet<string>();
            foreach (var hi in Hid.Enumerate(VID, Pids).Where(h => h.UsagePage == 0x0001 && h.Usage == 0x0005))
            {
                try
                {
                    // Bluetooth-Schnittstelle meldet 78-Byte-Berichte (0x31), USB 64 Byte. Windows fuellt kuerzere
                    // Berichte mit Nullen auf – daher NICHT an der gelesenen Laenge unterscheiden.
                    bool bt = hi.InLen >= 78;
                    string serial = (hi.Serial ?? "").Replace(":", "").Replace("-", "").ToUpperInvariant();
                    int? pct = null; bool chg = false;
                    string id;
                    using (var d = HidDevice.Open(hi))
                    {
                        if (d == null) continue;
                        if (serial.Length == 0 && !bt)
                        {
                            // USB: MAC aus dem Kopplungs-Bericht 0x09 (gleiche ID wie ueber Bluetooth). Nur ueber USB –
                            // ueber Bluetooth wuerde dieser Bericht den Controller umschalten.
                            var f = d.GetFeature(0x09);
                            if (f != null && f.Length >= 7)
                                serial = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}{4:X2}{5:X2}", f[6], f[5], f[4], f[3], f[2], f[1]);
                        }
                        id = "ds:" + (serial.Length > 0 ? serial : hi.Path.GetHashCode().ToString("X8"));
                        if (!seen.Add(id)) continue;
                        var end = Environment.TickCount + 300;
                        while (Environment.TickCount - end < 0)
                        {
                            var r = d.Read(Math.Max(1, end - Environment.TickCount));
                            if (r == null) break;
                            int at = !bt && r[0] == 0x01 && r.Length >= 64 ? 53  // USB: Bericht 0x01
                                   : bt && r[0] == 0x31 && r.Length >= 78 ? 54   // Bluetooth: voller Bericht 0x31
                                   : -1;                                         // Bluetooth-Kurzbericht 0x01 ohne Akku
                            if (at < 0) continue;
                            int s = r[at], level = s & 0x0F, state = s >> 4;
                            if (state == 2) { pct = 100; chg = false; }
                            else if (state <= 1) { pct = Math.Min(100, level * 10 + 5); chg = state == 1; }
                            break;
                        }
                    }
                    list.Add(new DeviceInfo
                    {
                        Id = id,
                        Name = hi.Pid == 0x0DF2 ? "DualSense Edge" : "DualSense",
                        Source = bt ? "DualSense (Bluetooth)" : "DualSense (USB)",
                        Percent = pct,
                        Charging = chg,
                        Connected = true,
                        DefaultIcon = "Controller"
                    });
                }
                catch (Exception ex) { Program.LogError(ex); }
            }
            return list;
        }
    }
}
