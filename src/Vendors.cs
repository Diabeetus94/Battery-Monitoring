// Erkennung installierter Hersteller-Software (gemeinsam fuer App und Setup)
// Es zaehlen NUR wirklich installierte Programme (Eintrag unter "Apps & Features").
// Dienste/Prozesse allein reichen nicht (z.B. Razer-Dienste, die andere Programme mitbringen).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AkkuAnzeige
{
    public enum VendorSupport { Supported, DetectedOnly }

    public class VendorInfo
    {
        public string Key;           // z.B. "ghub" (fuer die Verknuepfung)
        public string Name;          // z.B. "Logitech G HUB"
        public bool Found;
        public bool Running;
        public string Version;
        public VendorSupport Support;
        public string NoteDe, NoteEn;
        public bool Portable;        // nicht installiert, nur laufend erkannt (LGSTray)
    }

    public static class Vendors
    {
        class Def
        {
            public string Key, Name, UninstallRegex, ProcessRegex, NoteDe, NoteEn;
            public VendorSupport Support;
            public bool Portable;
        }

        static readonly Def[] defs =
        {
            new Def { Key = "ghub", Name = "Logitech G HUB", UninstallRegex = @"^Logitech G HUB", ProcessRegex = @"^lghub",
                      Support = VendorSupport.Supported,
                      NoteDe = "Akkustand und Aktiv-Status von Logitech-Mäusen, -Tastaturen und -Headsets",
                      NoteEn = "Battery level and active state of Logitech mice, keyboards and headsets" },
            new Def { Key = "lgstray", Name = "LGSTray", ProcessRegex = @"^LGSTray", Portable = true,
                      Support = VendorSupport.Supported,
                      NoteDe = "Ersatz-Quelle für Logitech, falls G HUB nicht läuft",
                      NoteEn = "Fallback source for Logitech if G HUB is not running" },
            new Def { Key = "synapse", Name = "Razer Synapse", UninstallRegex = @"^Razer Synapse", ProcessRegex = @"^Razer(Central|Synapse)",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Akkustände von Razer werden noch nicht gelesen",
                      NoteEn = "not compatible – Razer battery levels are not read yet" },
            new Def { Key = "icue", Name = "Corsair iCUE", UninstallRegex = @"^(Corsair )?iCUE", ProcessRegex = @"^iCUE",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Akkustände von Corsair werden noch nicht gelesen",
                      NoteEn = "not compatible – Corsair battery levels are not read yet" },
            new Def { Key = "steelseries", Name = "SteelSeries GG", UninstallRegex = @"^SteelSeries GG", ProcessRegex = @"^SteelSeriesGG",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Akkustände von SteelSeries werden noch nicht gelesen",
                      NoteEn = "not compatible – SteelSeries battery levels are not read yet" },
            new Def { Key = "optionsplus", Name = "Logitech Options+", UninstallRegex = @"^Logi Options", ProcessRegex = @"^logioptionsplus",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Geräte sind nur über Bluetooth sichtbar",
                      NoteEn = "not compatible – devices are only visible via Bluetooth" },
            new Def { Key = "ngenuity", Name = "HyperX NGENUITY", UninstallRegex = @"NGENUITY", ProcessRegex = @"NGenuity",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Akkustände von HyperX werden noch nicht gelesen",
                      NoteEn = "not compatible – HyperX battery levels are not read yet" },
            new Def { Key = "armoury", Name = "ASUS Armoury Crate", UninstallRegex = @"^Armoury Crate", ProcessRegex = @"^ArmouryCrate",
                      Support = VendorSupport.DetectedOnly,
                      NoteDe = "nicht kompatibel – Akkustände von ASUS werden noch nicht gelesen",
                      NoteEn = "not compatible – ASUS battery levels are not read yet" }
        };

        static List<KeyValuePair<string, string>> UninstallEntries()
        {
            var res = new List<KeyValuePair<string, string>>();
            var roots = new[]
            {
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64 },
                new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32 },
                new { Hive = RegistryHive.CurrentUser, View = RegistryView.Default }
            };
            foreach (var r in roots)
            {
                try
                {
                    using (var bk = RegistryKey.OpenBaseKey(r.Hive, r.View))
                    using (var k = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (k == null) continue;
                        foreach (var sub in k.GetSubKeyNames())
                        {
                            using (var s = k.OpenSubKey(sub))
                            {
                                var n = s == null ? null : s.GetValue("DisplayName") as string;
                                if (!string.IsNullOrEmpty(n)) res.Add(new KeyValuePair<string, string>(n, s.GetValue("DisplayVersion") as string ?? ""));
                            }
                        }
                    }
                }
                catch { }
            }
            return res;
        }

        // includePortable: auch laufende, nicht installierte Tools (LGSTray) – nur in der App, nicht im Setup
        public static List<VendorInfo> Detect(bool includePortable)
        {
            var uninst = UninstallEntries();
            string[] procs = new string[0];
            try { procs = Process.GetProcesses().Select(p => p.ProcessName).ToArray(); } catch { }

            var list = new List<VendorInfo>();
            foreach (var d in defs)
            {
                var vi = new VendorInfo { Key = d.Key, Name = d.Name, Support = d.Support, NoteDe = d.NoteDe, NoteEn = d.NoteEn, Portable = d.Portable };
                bool running = d.ProcessRegex != null && procs.Any(p => Regex.IsMatch(p, d.ProcessRegex, RegexOptions.IgnoreCase));
                if (d.Portable)
                {
                    if (!includePortable || !running) continue;
                    vi.Found = true; vi.Running = true;
                }
                else
                {
                    var hit = uninst.FirstOrDefault(u => Regex.IsMatch(u.Key, d.UninstallRegex, RegexOptions.IgnoreCase));
                    if (hit.Key == null) continue; // nicht installiert -> nicht anzeigen
                    vi.Found = true; vi.Version = hit.Value; vi.Running = running;
                }
                list.Add(vi);
            }
            return list;
        }
    }

    // Welche Datenquellen mit der Akku-Anzeige verknuepft sind (HKCU\Software\AkkuAnzeige\Links)
    public static class Links
    {
        const string RegPath = @"Software\AkkuAnzeige\Links";
        public const string Bluetooth = "bluetooth";

        // null = noch nie gefragt
        public static bool? Get(string key)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    var v = k == null ? null : k.GetValue(key);
                    if (v == null) return null;
                    return Convert.ToInt32(v) != 0;
                }
            }
            catch { return null; }
        }

        public static void Set(string key, bool on)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(RegPath)) k.SetValue(key, on ? 1 : 0, RegistryValueKind.DWord); } catch { }
        }

        // nicht gefragt = an (bisheriges Verhalten)
        public static bool Enabled(string key) { return Get(key) ?? true; }
    }
}
