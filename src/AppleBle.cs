// Akku-Anzeige: AirPods / Beats – Akkustand aus den Bluetooth-LE-Werbepaketen ("Proximity Pairing").
// Windows kennt den Akkustand von AirPods nicht; die Ohrhoerer senden ihn aber offen per BLE (Apple 0x004C, Typ 0x07).
// Der Scanner laeuft nur, solange ein Apple-Audiogeraet per Bluetooth verbunden ist (passiv, ohne Verbindung).
using System;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace AkkuAnzeige
{
    public class ApplePods
    {
        public int? Left, Right, Case;          // Prozent (10er-Schritte) oder null = unbekannt
        public bool LeftCharging, RightCharging, CaseCharging;
        public DateTime Seen;
        public short Rssi;
    }

    static class AppleBle
    {
        const ushort APPLE = 0x004C;
        const int MinRssi = -80;               // weiter entfernte (fremde) AirPods ignorieren
        static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

        static BluetoothLEAdvertisementWatcher watcher;
        static readonly object sync = new object();
        static readonly Dictionary<int, ApplePods> latest = new Dictionary<int, ApplePods>(); // je Modell-ID

        static DateTime retryAfter = DateTime.MinValue;

        // an = Scanner starten (falls noch nicht), aus = stoppen
        public static void Watch(bool on)
        {
            lock (sync)
            {
                // abgebrochen (z.B. Bluetooth kurz aus) -> neu starten
                if (watcher != null && (!on || watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted))
                {
                    try { watcher.Received -= OnReceived; watcher.Stop(); } catch { }
                    watcher = null;
                    if (!on) latest.Clear();
                }
                if (on && watcher == null && DateTime.Now >= retryAfter)
                {
                    try
                    {
                        var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
                        // schon im Bluetooth-Stack filtern: nur Apple (4C 00) + Proximity Pairing (07 19) –
                        // iPhones, Macs usw. in der Naehe wecken die App so gar nicht erst auf
                        var dw = new DataWriter();
                        dw.WriteBytes(new byte[] { 0x4C, 0x00, 0x07, 0x19 });
                        w.AdvertisementFilter.BytePatterns.Add(new BluetoothLEAdvertisementBytePattern { DataType = 0xFF, Offset = 0, Data = dw.DetachBuffer() });
                        w.Received += OnReceived;
                        w.Start();
                        watcher = w;
                    }
                    catch (Exception ex)
                    {
                        retryAfter = DateTime.Now.AddMinutes(5); // nicht alle 5 s erneut versuchen (und protokollieren)
                        Program.LogError(ex);
                    }
                }
            }
        }

        // letzter frischer Stand fuer das Modell (= PID des verbundenen Bluetooth-Geraets)
        public static ApplePods Get(int model)
        {
            lock (sync)
            {
                ApplePods p;
                return latest.TryGetValue(model, out p) && DateTime.Now - p.Seen < MaxAge ? p : null;
            }
        }

        static void OnReceived(BluetoothLEAdvertisementWatcher w, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            try
            {
                if (e.RawSignalStrengthInDBm < MinRssi) return;
                foreach (var md in e.Advertisement.GetManufacturerDataByCompanyId(APPLE))
                {
                    if (md.Data.Length != 27) continue;
                    var b = new byte[27];
                    using (var r = DataReader.FromBuffer(md.Data)) r.ReadBytes(b);
                    if (b[0] != 0x07 || b[1] != 0x19) continue; // Proximity Pairing
                    int model = b[3] | (b[4] << 8);
                    bool thisIsLeft = (b[5] & 0x20) != 0;
                    int thisBud = b[6] & 0x0F, otherBud = b[6] >> 4, caseLvl = b[7] & 0x0F;
                    bool thisChg = (b[7] & 0x10) != 0, otherChg = (b[7] & 0x20) != 0;
                    var p = new ApplePods
                    {
                        Left = Pct(thisIsLeft ? thisBud : otherBud),
                        Right = Pct(thisIsLeft ? otherBud : thisBud),
                        Case = Pct(caseLvl),
                        LeftCharging = thisIsLeft ? thisChg : otherChg,
                        RightCharging = thisIsLeft ? otherChg : thisChg,
                        CaseCharging = (b[7] & 0x40) != 0,
                        Seen = DateTime.Now,
                        Rssi = e.RawSignalStrengthInDBm
                    };
                    lock (sync)
                    {
                        ApplePods old;
                        // mehrere gleiche Modelle in der Naehe: das staerkere Signal (= naeher, meist die eigenen) gewinnt
                        if (latest.TryGetValue(model, out old) && DateTime.Now - old.Seen < TimeSpan.FromSeconds(5) && old.Rssi > p.Rssi + 10) continue;
                        latest[model] = p;
                    }
                }
            }
            catch { }
        }

        // 0-10 -> 0-100 %, 11-15 = unbekannt
        static int? Pct(int v) { return v <= 10 ? (int?)(v * 10) : null; }
    }
}
