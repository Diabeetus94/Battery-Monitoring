// Akku-Anzeige: Einstellungen, Kachel-Leisten und Einstellungsfenster (je Monitor eigene Konfiguration)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AkkuAnzeige
{
    // ======================= Einstellungen =======================
    public class DeviceEntry
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public DeviceEntry Clone() { return new DeviceEntry { Id = Id, Label = Label, Icon = Icon }; }
    }

    public class MonitorEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Size { get; set; }                 // Breite der Kacheln in %
        public bool Enabled { get; set; }             // Leiste auf diesem Monitor anzeigen
        public List<DeviceEntry> Devices { get; set; } // Geraete dieses Monitors (Reihenfolge = Kacheln)
        public MonitorEntry() { Size = 100; }
        public MonitorEntry Clone()
        {
            return new MonitorEntry
            {
                Id = Id, Name = Name, Size = Size, Enabled = Enabled,
                Devices = Devices == null ? new List<DeviceEntry>() : Devices.Select(d => d.Clone()).ToList()
            };
        }
    }

    public class AppSettings
    {
        public int WarnLevel { get; set; }
        public int TileWidth { get; set; }
        public List<MonitorEntry> KnownMonitors { get; set; }
        public bool StartMinimized { get; set; } // beim Start kein Einstellungsfenster
        public bool CloseExits { get; set; }     // X am Fenster beendet das ganze Programm
        public int OrangeFrom { get; set; }      // ab hier orange (darunter rot)
        public int GreenFrom { get; set; }       // ab hier gruen
        public bool GrayOffline { get; set; }    // getrennte/schlafende Geraete grau anzeigen
        public Dictionary<string, int> LastPercent { get; set; } // letzter bekannter Akkustand je Geraet
        public string Language { get; set; }     // "de" / "en"
        public bool ThemeAuto { get; set; }      // Fensterdesign folgt Windows (hell/dunkel)
        public bool ThemeDark { get; set; }      // manuelle Wahl, wenn ThemeAuto aus
        public bool IdleGray { get; set; }       // Maus/Tastatur grau, wenn X Minuten unbenutzt (Beleuchtung aus)
        public int IdleMinutes { get; set; }
        public int AlarmVolume { get; set; }     // Alarm-Lautstaerke 0-100 %
        public int ColorRed { get; set; }        // eigene Farben als ARGB, 0 = Standardfarbe (siehe Palette)
        public int ColorOrange { get; set; }
        public int ColorGreen { get; set; }
        public int ColorCharging { get; set; }
        public string AlarmFile { get; set; }    // eigene Sounddatei (leer = Standard-Warnton)
        // aeltere Versionen (nur zum Uebernehmen beim ersten Start)
        public List<DeviceEntry> Devices { get; set; }
        public bool AllMonitors { get; set; }
        public List<string> Monitors { get; set; }
        // true, sobald der Nutzer die Geraeteauswahl einmal uebernommen hat; vorher werden gefundene
        // Geraete automatisch auf leeren Monitor-Leisten angezeigt (damit nach der Installation etwas zu sehen ist)
        public bool DevicesConfigured { get; set; }

        public AppSettings()
        {
            WarnLevel = 25;
            TileWidth = 175;
            OrangeFrom = 30;
            GreenFrom = 70;
            GrayOffline = true;
            ThemeAuto = true;
            Language = "de";
            AlarmVolume = 80;
            IdleGray = false; // Standard: nur echtes Ausschalten/Standby (G HUB) zaehlt als inaktiv
            IdleMinutes = 2;
            LastPercent = new Dictionary<string, int>();
            KnownMonitors = new List<MonitorEntry>();
            Devices = new List<DeviceEntry>();
            Monitors = new List<string>();
        }

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        // Neue Monitore starten ohne Geraete – gefundene Geraete kommen automatisch dazu (siehe DevicesConfigured)
        public static List<DeviceEntry> DefaultDevices() { return new List<DeviceEntry>(); }

        // Fruehere Versionen haben auf jedem PC fest drei Beispielgeraete eingetragen (die Geraete des Autors).
        // Die entfernen, wenn sie auf diesem PC nie gesehen wurden.
        static readonly string[] OldSampleIds = { "logi:G502", "logi:G915", "bt:7CE9132D4420" };
        void DropUnseenSamples(List<DeviceEntry> list)
        {
            if (list != null) list.RemoveAll(d => OldSampleIds.Contains(d.Id) && !LastPercent.ContainsKey(d.Id));
        }

        public static AppSettings Load()
        {
            AppSettings s = null;
            bool fresh = false, hadFlag = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    s = new JavaScriptSerializer().Deserialize<AppSettings>(json);
                    hadFlag = json.Contains("\"DevicesConfigured\"");
                }
            }
            catch { }
            if (s == null) { s = new AppSettings(); fresh = true; }
            if (s.KnownMonitors == null) s.KnownMonitors = new List<MonitorEntry>();
            if (s.Devices == null) s.Devices = new List<DeviceEntry>();
            if (s.Monitors == null) s.Monitors = new List<string>();
            if (s.TileWidth < 120) s.TileWidth = 175;
            if (s.WarnLevel < 1 || s.WarnLevel > 99) s.WarnLevel = 25;
            if (s.LastPercent == null) s.LastPercent = new Dictionary<string, int>();
            if (s.IdleMinutes < 1 || s.IdleMinutes > 60) s.IdleMinutes = 2;
            if (!L.Languages.Any(kv => kv.Key == s.Language)) s.Language = "de";
            if (s.OrangeFrom < 1 || s.OrangeFrom > 99 || s.GreenFrom <= s.OrangeFrom || s.GreenFrom > 100) { s.OrangeFrom = 30; s.GreenFrom = 70; }

            var cur = Displays.GetAll();
            // alte Monitor-Angaben (\\.\DISPLAYx oder Geraetepfad)
            var legacyMons = new List<string>();
            foreach (var m in s.Monitors)
            {
                if (m.StartsWith(@"\\.\"))
                {
                    var hit = cur.FirstOrDefault(x => x.GdiName == m);
                    if (hit != null) legacyMons.Add(hit.Id);
                }
                else legacyMons.Add(m);
            }
            var legacyDevices = s.Devices.Count > 0 ? s.Devices : DefaultDevices();
            s.DropUnseenSamples(legacyDevices);

            foreach (var k in s.KnownMonitors)
            {
                if (k.Devices == null) // aus alter Version uebernehmen
                {
                    k.Devices = legacyDevices.Select(d => d.Clone()).ToList();
                    k.Enabled = s.AllMonitors || legacyMons.Contains(k.Id);
                }
                if (k.Size < 50 || k.Size > 200) k.Size = 100;
                s.DropUnseenSamples(k.Devices);
            }
            foreach (var mi in cur)
            {
                if (s.KnownMonitors.Any(k => k.Id == mi.Id)) continue;
                s.KnownMonitors.Add(new MonitorEntry
                {
                    Id = mi.Id, Name = mi.Name, Size = 100,
                    Devices = legacyDevices.Select(d => d.Clone()).ToList(),
                    Enabled = fresh ? mi.GdiName == Screen.PrimaryScreen.DeviceName : (s.AllMonitors || legacyMons.Contains(mi.Id))
                });
            }
            s.Devices = new List<DeviceEntry>();
            s.Monitors = new List<string>();
            s.AllMonitors = false;
            // Einstellungen aus aelteren Versionen: wer schon eigene Geraete ausgewaehlt hat, bekommt nichts automatisch dazu
            if (!fresh && !hadFlag) s.DevicesConfigured = s.KnownMonitors.Any(k => k.Devices != null && k.Devices.Count > 0);
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
        }
    }

    // ======================= Hauptlogik =======================
    class AppCtx : ApplicationContext
    {
        public AppSettings Settings;
        public float Scale = 1f;
        public Icon AppIcon;
        public Color TileBg, AlarmBg, Fg, BarBg;
        public List<DeviceInfo> Available = new List<DeviceInfo>();

        Dictionary<string, DeviceInfo> data = new Dictionary<string, DeviceInfo>();
        List<BarForm> bars = new List<BarForm>();
        HashSet<string> warned = new HashSet<string>();
        List<MonitorEntry> preview; // != null: Vorschau aus dem Einstellungsfenster
        InputActivity input;        // letzte Eingabe je Empfaenger (nur Zeitstempel)
        NotifyIcon tray;
        ContextMenuStrip menu;
        Control invoker;
        System.Windows.Forms.Timer pollTimer, keepTimer;
        SettingsForm settingsForm;
        volatile bool polling;
        string screenSig = "";

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public AppCtx(EventWaitHandle showEvt, bool showSettings)
        {
            Settings = AppSettings.Load();
            try { Settings.Save(); } catch { }
            L.Current = Settings.Language;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;

            ReadTileColors();

            invoker = new Control();
            var h = invoker.Handle;
            try { input = new InputActivity(); } catch (Exception ex) { Program.LogError(ex); }

            AppIcon = LoadAppIcon();
            menu = new ContextMenuStrip();
            RebuildMenu();

            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = "Akku-Anzeige";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowSettings(); };

            taskbarHook = new TaskbarHook(OnTaskbarMoved, OnForeground);
            RebuildBars();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged += OnUserPrefChanged;

            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 5000; // G HUB + Bluetooth sind lokal und schnell -> Statuswechsel nach max. ~5 s sichtbar
            pollTimer.Tick += delegate { Poll(); };
            pollTimer.Start();

            keepTimer = new System.Windows.Forms.Timer();
            // nur noch Sicherheitsnetz: Fensterwechsel (Vollbild-Spiel, Klick auf die Taskleiste) und Taskleisten-
            // Bewegungen kommen sofort ueber TaskbarHook; frueher 300 ms
            keepTimer.Interval = 1000;
            keepTimer.Tick += delegate { Keep(); };
            keepTimer.Start();

            var t = new Thread(() =>
            {
                while (true)
                {
                    showEvt.WaitOne();
                    try { invoker.BeginInvoke((Action)ShowSettings); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();

            Poll();
            if (showSettings && !Settings.StartMinimized) ShowSettings();

            // neu installierte, kompatible Treiber-Software erkennen und nachfragen (beim Start + alle 30 Min.)
            CheckNewVendors();
            vendorTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
            vendorTimer.Tick += delegate { CheckNewVendors(); };
            vendorTimer.Start();
        }

        public Icon WindowIcon;

        // Kachelfarben passend zur Taskleiste (Windows-Modus hell/dunkel)
        void ReadTileColors()
        {
            bool light = false;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) light = k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1; } catch { }
            TileBg = light ? Color.FromArgb(228, 228, 228) : Color.FromArgb(48, 48, 48);
            AlarmBg = light ? Color.FromArgb(255, 205, 205) : Color.FromArgb(95, 30, 30);
            Fg = light ? Color.FromArgb(20, 20, 20) : Color.White;
            BarBg = light ? Color.FromArgb(190, 190, 190) : Color.FromArgb(85, 85, 85);
        }

        void OnUserPrefChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            try
            {
                invoker.BeginInvoke((Action)(() =>
                {
                    ReadTileColors();
                    foreach (var b in bars) b.Invalidate();
                    if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.SystemThemeChanged();
                }));
            }
            catch { }
        }

        static Icon LoadIconRes(string name)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    if (s != null) return new Icon(s);
            }
            catch { }
            return null;
        }

        Icon LoadAppIcon()
        {
            // Fenster-Symbol (oben links / Taskleiste): Bluetooth_Batterie_Minimal.ico
            WindowIcon = LoadIconRes("AkkuAnzeige.win.ico");
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AkkuAnzeige.app.ico"))
                    if (s != null)
                    {
                        var ic = new Icon(s, SystemInformation.SmallIconSize);
                        if (WindowIcon == null) WindowIcon = ic;
                        return ic;
                    }
            }
            catch { }
            if (WindowIcon == null) WindowIcon = SystemIcons.Application;
            return SystemIcons.Application;
        }

        public void ShowMenu(Point p) { menu.Show(p); }

        System.Windows.Forms.Timer vendorTimer;
        bool askingVendors;

        void CheckNewVendors()
        {
            if (askingVendors) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<VendorInfo> found;
                try { found = Vendors.Detect(true).Where(v => v.Support == VendorSupport.Supported && Links.Get(v.Key) == null).ToList(); }
                catch { return; }
                if (found.Count == 0) return;
                try
                {
                    invoker.BeginInvoke((Action)(() =>
                    {
                        askingVendors = true;
                        foreach (var v in found)
                        {
                            var r = MessageBox.Show(L.F("{0} wurde gefunden und ist mit der Akku-Anzeige kompatibel.\n\n{1}\n\nJetzt verknüpfen?", v.Name, L.Current == "en" ? v.NoteEn : v.NoteDe),
                                                    "Akku-Anzeige", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1,
                                                    MessageBoxOptions.DefaultDesktopOnly);
                            Links.Set(v.Key, r == DialogResult.Yes);
                        }
                        askingVendors = false;
                        Poll();
                    }));
                }
                catch { }
            });
        }

        // Rechtsklick-Menue (Infobereich + Kacheln) in der aktuellen Sprache
        public void RebuildMenu()
        {
            menu.Items.Clear();
            menu.Items.Add(L.T("Einstellungen …"), null, delegate { ShowSettings(); });
            menu.Items.Add(L.T("Info …"), null, delegate { ShowSettings(); if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.ShowInfo(); });
            menu.Items.Add(L.T("Jetzt aktualisieren"), null, delegate { Poll(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Beenden"), null, delegate { Quit(); });
        }

        public void ShowSettings(int tab)
        {
            ShowSettings();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.SelectTab(tab);
        }

        public void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this);
            if (!settingsForm.Visible) settingsForm.Show();
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate();
            settingsForm.BringToFront();
        }

        // ----- Daten -----
        public void Poll()
        {
            if (polling) return;
            polling = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DeviceInfo> l;
                try { l = DeviceReader.ReadAll(); } catch { l = new List<DeviceInfo>(); }
                try { invoker.BeginInvoke((Action)(() => { polling = false; Apply(l); })); }
                catch { polling = false; }
            });
        }

        void Apply(List<DeviceInfo> l)
        {
            Available = l;
            data = new Dictionary<string, DeviceInfo>();
            foreach (var d in l) if (!data.ContainsKey(d.Id)) data[d.Id] = d;
            // letzten bekannten Akkustand merken (fuer getrennte / schlafende Geraete)
            bool changed = false;
            foreach (var d in l)
            {
                if (!d.Connected || !d.Percent.HasValue) continue;
                int old;
                if (!Settings.LastPercent.TryGetValue(d.Id, out old) || old != d.Percent.Value) { Settings.LastPercent[d.Id] = d.Percent.Value; changed = true; }
            }
            if (AutoAddDevices(l)) { changed = true; RebuildBars(); }
            if (changed) { try { Settings.Save(); } catch { } }
            foreach (var b in bars) b.Invalidate();
            CheckAlarms();
            UpdateTrayText();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.UpdateDevices(l);
        }

        // Bis der Nutzer selbst Geraete auswaehlt: gefundene Geraete auf den aktivierten Leisten anzeigen
        bool AutoAddDevices(List<DeviceInfo> l)
        {
            // bei offenem Einstellungsfenster haken die Monitor-Reiter neue Geraete selbst an (sonst wuerde
            // die Fenster-Kopie der Auswahl die Ergaenzungen beim naechsten Vorschau-Schritt wieder verwerfen)
            if (Settings.DevicesConfigured || preview != null || l.Count == 0) return false;
            if (settingsForm != null && !settingsForm.IsDisposed) return false;
            bool added = false;
            foreach (var m in Settings.KnownMonitors.Where(x => x.Enabled && x.Devices != null))
                foreach (var d in l)
                    if (!m.Devices.Any(x => x.Id == d.Id))
                    {
                        m.Devices.Add(new DeviceEntry { Id = d.Id, Label = d.Name, Icon = d.DefaultIcon ?? "Bluetooth" });
                        added = true;
                    }
            return added;
        }

        public DeviceInfo Get(string id) { DeviceInfo d; return data.TryGetValue(id, out d) ? d : null; }
        // nur verbundene Geraete (fuer den Alarm)
        public int? PercentOf(string id) { var d = Get(id); return d != null && d.Connected ? d.Percent : null; }
        // aktiv = verbunden UND (bei Logitech) in den letzten X Minuten benutzt
        public bool IsOnline(string id)
        {
            var d = Get(id);
            if (d == null || !d.Connected || !d.Percent.HasValue) return false;
            if (IdleGray && d.InputKey != null && !d.Charging && InputActivity.IdleSeconds(d.InputKey) > IdleMinutes * 60) return false;
            return true;
        }
        // anzuzeigender Wert: aktuell, sonst zuletzt bekannter
        public int? DisplayPercent(string id)
        {
            var d = Get(id);
            if (d != null && d.Percent.HasValue) return d.Percent;
            int v;
            return Settings.LastPercent.TryGetValue(id, out v) ? (int?)v : null;
        }

        // Grenzwerte (mit Vorschau aus dem Einstellungsfenster)
        int? pvWarn, pvOrange, pvGreen, pvIdleMin; bool? pvGray, pvIdle;
        public bool IdleGray { get { return pvIdle ?? Settings.IdleGray; } }
        public int IdleMinutes { get { return pvIdleMin ?? Settings.IdleMinutes; } }
        public void SetIdlePreview(bool on, int minutes)
        {
            pvIdle = on; pvIdleMin = minutes;
            foreach (var b in bars) b.Invalidate();
        }
        public int WarnLevel { get { return pvWarn ?? Settings.WarnLevel; } }
        public int OrangeFrom { get { return pvOrange ?? Settings.OrangeFrom; } }
        public int GreenFrom { get { return pvGreen ?? Settings.GreenFrom; } }
        public bool GrayOffline { get { return pvGray ?? Settings.GrayOffline; } }

        public void SetLimitPreview(int warn, int orange, int green, bool gray)
        {
            pvWarn = warn; pvOrange = orange; pvGreen = green; pvGray = gray;
            foreach (var b in bars) b.Invalidate();
        }

        public Color LevelColor(int pct)
        {
            if (pct >= GreenFrom) return ColorOf(Palette.Green);
            if (pct >= OrangeFrom) return ColorOf(Palette.Orange);
            return ColorOf(Palette.Red);
        }

        // eigene Farben (Grenzwerte -> Farben), mit Vorschau aus dem Einstellungsfenster
        int[] pvColors;
        public int[] SavedColors { get { return new[] { Settings.ColorRed, Settings.ColorOrange, Settings.ColorGreen, Settings.ColorCharging }; } }
        public Color ColorOf(int slot)
        {
            int v = (pvColors ?? SavedColors)[slot];
            return v == 0 ? Palette.Default[slot] : Color.FromArgb(255, Color.FromArgb(v));
        }
        public void SetColorPreview(int[] colors)
        {
            pvColors = colors;
            foreach (var b in bars) b.Invalidate();
        }

        // alle Geraete, die auf irgendeinem aktivierten Monitor gewaehlt sind
        List<DeviceEntry> WatchedDevices()
        {
            var res = new List<DeviceEntry>();
            foreach (var m in Settings.KnownMonitors.Where(x => x.Enabled))
                foreach (var d in m.Devices)
                    if (!res.Any(r => r.Id == d.Id)) res.Add(d);
            return res;
        }

        void CheckAlarms()
        {
            var low = new List<KeyValuePair<string, int>>();
            foreach (var e in WatchedDevices())
            {
                var d = Get(e.Id);
                int? p = PercentOf(e.Id);
                if (!p.HasValue) continue;
                if (!warned.Contains(e.Id) && !d.Charging && p.Value <= WarnLevel)
                {
                    warned.Add(e.Id);
                    low.Add(new KeyValuePair<string, int>(e.Label, p.Value));
                }
                else if (warned.Contains(e.Id) && (d.Charging || p.Value >= WarnLevel + 5))
                {
                    warned.Remove(e.Id);
                }
            }
            if (low.Count == 1)
                ShowAlarm(L.F("Akku fast leer: {0}", low[0].Key), L.F("{0} hat nur noch {1} % Akku – bitte aufladen!", low[0].Key, low[0].Value));
            else if (low.Count > 1)
                ShowAlarm(L.F("Akku fast leer: {0}", string.Join(", ", low.Select(x => x.Key))),
                          string.Join("\n", low.Select(x => L.F("{0}: noch {1} %", x.Key, x.Value))) + "\n" + L.T("Bitte aufladen!"));
        }

        List<AlarmForm> alarmForms = new List<AlarmForm>();

        public void ShowAlarm(string title, string text) { ShowAlarm(title, text, null, null); }

        public void ShowAlarm(string title, string text, string soundFile, int? volume)
        {
            // 1) eigenes Warnfenster + Warnton: unabhaengig von "Nicht stoeren", Spielmodus und
            //    stummgeschalteten Benachrichtigungen (laeuft nicht ueber das Windows-Benachrichtigungssystem)
            var old = alarmForms.ToList();
            alarmForms.Clear();
            foreach (var f in old) { try { f.Close(); } catch { } }
            foreach (var s in Screen.AllScreens)
            {
                var f = new AlarmForm(this, s, title, text);
                f.FormClosed += (o, e) =>
                {
                    // ein Fenster bestaetigt -> alle schliessen
                    var all = alarmForms.ToList();
                    alarmForms.Clear();
                    foreach (var x in all) if (x != o) { try { x.Close(); } catch { } }
                };
                alarmForms.Add(f);
                f.Show();
            }
            AlarmSound.Play(soundFile ?? Settings.AlarmFile, volume ?? Settings.AlarmVolume);
            // (keine zusaetzliche Windows-Benachrichtigung mehr)
        }

        void UpdateTrayText()
        {
            var parts = WatchedDevices().Select(e => { var p = DisplayPercent(e.Id); return e.Label + " " + (p.HasValue ? p.Value + "%" : "--"); });
            string t = string.Join(" | ", parts);
            if (t.Length == 0) t = "Akku-Anzeige";
            if (t.Length > 63) t = t.Substring(0, 60) + "...";
            tray.Text = t;
        }

        // ----- Monitore / Kacheln -----
        static string ScreenSig()
        {
            return string.Join(";", Screen.AllScreens.Select(s => s.DeviceName + s.Bounds.ToString()));
        }

        public bool RememberMonitors(List<MonitorInfo> mons)
        {
            bool changed = false;
            foreach (var mi in mons)
            {
                var k = Settings.KnownMonitors.FirstOrDefault(x => x.Id == mi.Id);
                if (k == null)
                {
                    Settings.KnownMonitors.Add(new MonitorEntry { Id = mi.Id, Name = mi.Name, Size = 100, Enabled = false, Devices = AppSettings.DefaultDevices() });
                    changed = true;
                }
                else if (k.Name != mi.Name) { k.Name = mi.Name; changed = true; }
            }
            return changed;
        }

        public void SetPreview(List<MonitorEntry> p) { preview = p; RebuildBars(); }
        public void EndPreview()
        {
            pvWarn = null; pvOrange = null; pvGreen = null; pvGray = null; pvIdle = null; pvIdleMin = null; pvColors = null;
            preview = null;
            RebuildBars();
        }

        public void RebuildBars()
        {
            screenSig = ScreenSig();
            var mons = Displays.GetAll();
            if (RememberMonitors(mons)) { try { Settings.Save(); } catch { } }
            var config = preview ?? Settings.KnownMonitors;
            // Vorhandene Leisten weiterverwenden und nur die Konfiguration tauschen: ein NEU angelegtes Fenster
            // nimmt dem Einstellungsfenster den Tastaturfokus (sonst muss man nach jedem Buchstaben im
            // Anzeigenamen wieder ins Feld klicken, weil die Vorschau die Leisten neu aufbaut).
            var keep = new List<BarForm>();
            foreach (var mi in mons)
            {
                if (!mi.Active || mi.GdiName == null) continue;
                var cfg = config.FirstOrDefault(x => x.Id == mi.Id);
                if (cfg == null || !cfg.Enabled || cfg.Devices == null || cfg.Devices.Count == 0) continue;
                if (!Screen.AllScreens.Any(s => s.DeviceName == mi.GdiName)) continue;
                var b = bars.FirstOrDefault(x => x.ScreenName == mi.GdiName && !keep.Contains(x));
                if (b == null)
                {
                    b = new BarForm(this, mi.GdiName, cfg);
                    b.UpdatePosition();
                    b.Show();
                    // Einhaengen in die Taskleiste (TryEmbed) funktioniert unter Windows 11 nicht:
                    // die XAML-Taskleiste uebermalt eingehaengte Fenster komplett -> immer-im-Vordergrund-Fenster
                }
                else b.SetConfig(cfg);
                keep.Add(b);
            }
            foreach (var b in bars.Where(x => !keep.Contains(x))) { b.Close(); b.Dispose(); }
            bars = keep;
            taskbarHook.Watch(bars.Count > 0);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            // nur bei echten Monitor-Aenderungen neu aufbauen (das Einhaengen in die Taskleiste
            // loest diese Meldung ebenfalls aus -> sonst Endlosschleife)
            try { invoker.BeginInvoke((Action)(() => { if (ScreenSig() != screenSig) RebuildBars(); })); } catch { }
        }

        void Keep()
        {
            if (ScreenSig() != screenSig) { RebuildBars(); return; } // Monitor ein-/ausgeschaltet
            string fullScreenDev = null;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && !bars.Any(b => b.Handle == fg))
            {
                // Startmenue, Infobereich, Schnelleinstellungen usw. sind KEIN Vollbild-Spiel
                // eigenes Fenster per Prozess-ID erkennen: Process.GetCurrentProcess().ProcessName zaehlt unter dem
                // .NET Framework bei jedem Aufruf alle Prozesse auf (gemessen 2,3 ms CPU – alle 300 ms der Grossteil
                // der Leerlauf-Last)
                if (!Native.IsOwnWindow(fg) && !Native.IsShellWindow(fg))
                {
                    Native.RECT r;
                    Native.GetWindowRect(fg, out r);
                    var s = Screen.FromHandle(fg);
                    // Ein maximiertes Fenster mit Titelleiste ist kein Vollbild – bei automatisch ausblendender
                    // Taskleiste bedeckt es aber ebenfalls den ganzen Bildschirm.
                    const int WS_CAPTION = 0x00C00000;
                    bool maximizedWindow = Native.IsZoomed(fg) && (Native.GetWindowLong(fg, -16) & WS_CAPTION) == WS_CAPTION;
                    if (!maximizedWindow && r.Left <= s.Bounds.Left && r.Top <= s.Bounds.Top && r.Right >= s.Bounds.Right && r.Bottom >= s.Bounds.Bottom)
                        fullScreenDev = s.DeviceName;
                }
            }
            if (bars.Any(b => b.EmbedBroken))
            {
                foreach (var b in bars.Where(x => x.EmbedBroken))
                    Program.Log("EmbedBroken " + b.ScreenName + " isWin=" + Native.IsWindow(b.Taskbar) + " parent=" + Native.GetParent(b.Handle) + " tb=" + b.Taskbar);
                RebuildBars();
                return;
            }
            fullScreen = fullScreenDev;
            taskbarHook.Watch(bars.Count > 0); // Explorer neu gestartet -> neu einhaengen
            foreach (var b in bars)
            {
                b.UpdatePosition();
                if (b.Embedded) continue; // Teil der Taskleiste: verschwindet bei Vollbild automatisch mit ihr
                ShowOnTop(b);
            }
        }

        TaskbarHook taskbarHook;
        string fullScreen; // Monitor mit Vollbild-Spiel (aus dem letzten Keep)

        void ShowOnTop(BarForm b)
        {
            bool hide = b.ScreenName == fullScreen || b.OffScreen;
            b.SetShown(!hide);
            // nur hochlegen, wenn wirklich ein sichtbares Fenster darueberliegt (SetWindowPos kostet sonst bei jedem
            // Durchlauf ~0,2 ms fuer nichts)
            if (!hide && Native.IsCovered(b.Handle)) Native.SetWindowPos(b.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        }

        // Leiste hat sich selbst bewegt (Gleiten nach dem Verlassen mit der Maus)
        public void BarMoved(BarForm b) { ShowOnTop(b); }

        // Taskleiste hat sich bewegt (Ein-/Ausfahren bei automatischem Ausblenden): sofort mitziehen
        // Taskleiste wird aktiviert (Klick darauf): sie schiebt sich dabei ueber alle Immer-im-Vordergrund-Fenster,
        // auch ueber die Kacheln, bis Keep() sie nach bis zu 300 ms zurueckholt (= sichtbares Wegblinken).
        // Jetzt: sofort wieder darueberlegen, und weil die Taskleiste sich manchmal erst kurz NACH dieser
        // Meldung nach vorn schiebt, wacht 200 ms lang ein Hintergrund-Thread im 1-ms-Takt (ein Fenster-Timer
        // waere mit ~16 ms zu grob). Gemessen: verdeckt nur noch 1-2 ms statt 60-330 ms. Laeuft nur nach
        // einer Taskleisten-Aktivierung, sonst nie.
        volatile int guardUntil; // wird vom Waechter-Thread gelesen
        Thread guard;

        void OnForeground(IntPtr hwnd)
        {
            Keep(); // Vollbild-Erkennung + Hochlegen sofort beim Fensterwechsel statt erst beim naechsten Timer
            string cls = Native.ClassOf(hwnd);
            if (cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd") return;
            guardUntil = Environment.TickCount + 200;
            if (guard != null && guard.IsAlive) return;
            var handles = bars.Where(b => !b.Embedded && b.Visible).Select(b => b.Handle).ToArray();
            guard = new Thread(() =>
            {
                Native.timeBeginPeriod(1);
                try
                {
                    while (Environment.TickCount - guardUntil < 0)
                    {
                        foreach (var h in handles)
                            if (Native.IsWindowVisible(h) && Native.CoveredByTopmost(h))
                                Native.SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
                        Thread.Sleep(1);
                    }
                }
                catch { }
                finally { Native.timeEndPeriod(1); }
            });
            guard.IsBackground = true;
            guard.Start();
        }

        void OnTaskbarMoved(IntPtr hwnd)
        {
            foreach (var b in bars)
            {
                if (b.TaskbarWnd != hwnd || b.Embedded) continue;
                b.UpdatePosition();
                ShowOnTop(b); // die Taskleiste holt sich beim Einfahren nach vorn -> wieder darueber legen
            }
        }

        // ----- Speichern -----
        public void ApplySettings(List<MonitorEntry> monitors, int warnLevel, bool startMin, bool closeExits, int orange, int green, bool gray, int[] colors)
        {
            Settings.ColorRed = colors[Palette.Red]; Settings.ColorOrange = colors[Palette.Orange];
            Settings.ColorGreen = colors[Palette.Green]; Settings.ColorCharging = colors[Palette.Charging];
            pvColors = null;
            Settings.StartMinimized = startMin;
            Settings.CloseExits = closeExits;
            Settings.OrangeFrom = orange;
            Settings.GreenFrom = green;
            Settings.GrayOffline = gray;
            pvWarn = null; pvOrange = null; pvGreen = null; pvGray = null; pvIdle = null; pvIdleMin = null;
            // Monitore, die das Fenster nicht kannte (z.B. gerade erst angeschlossen), behalten
            foreach (var k in Settings.KnownMonitors)
                if (!monitors.Any(m => m.Id == k.Id)) monitors.Add(k);
            Settings.KnownMonitors = monitors;
            Settings.DevicesConfigured = true;
            Settings.WarnLevel = warnLevel;
            Settings.Save();
            preview = null;
            warned.Clear();
            RebuildBars();
            CheckAlarms();
            UpdateTrayText();
        }

        public static bool AutostartEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("AkkuAnzeige") != null;
            }
        }

        public static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("AkkuAnzeige", "\"" + Application.ExecutablePath + "\" /autostart");
                else k.DeleteValue("AkkuAnzeige", false);
            }
        }

        public void BeginQuit() { try { invoker.BeginInvoke((Action)Quit); } catch { } }

        public void Quit()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPrefChanged;
            pollTimer.Stop(); keepTimer.Stop();
            guardUntil = Environment.TickCount;
            taskbarHook.Watch(false);
            tray.Visible = false; tray.Dispose();
            foreach (var b in bars) b.Close();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            ExitThread();
        }
    }

    // ======================= Kachel-Leiste =======================
    class BarForm : Form
    {
        AppCtx ctx;
        MonitorEntry cfg;
        public string ScreenName;
        Font fGlyph, fName, fPct, fSmallGlyph;
        float sc = -1; // Massstab aus der Taskleistenhoehe

        public BarForm(AppCtx c, string screenName, MonitorEntry config)
        {
            ctx = c;
            cfg = config;
            ScreenName = screenName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 0, 255);
            TransparencyKey = BackColor;
            DoubleBuffered = true;
            MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right) ctx.ShowMenu(Cursor.Position);
                else ctx.Poll();
            };
        }

        static Font MakeFont(string fam, string fallback, float px)
        {
            var f = new Font(fam, px, FontStyle.Regular, GraphicsUnit.Pixel);
            if (f.Name == fam) return f;
            f.Dispose();
            return new Font(fallback, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        void EnsureFonts(float s)
        {
            if (Math.Abs(s - sc) < 0.001f) return;
            sc = s;
            fGlyph = MakeFont("Segoe Fluent Icons", "Segoe MDL2 Assets", 17 * sc);
            fSmallGlyph = MakeFont("Segoe Fluent Icons", "Segoe MDL2 Assets", 11 * sc);
            fName = MakeFont("Segoe UI Variable Text", "Segoe UI", 12 * sc);
            fPct = MakeFont("Segoe UI Variable Text Semibold", "Segoe UI Semibold", 14 * sc);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        // ---- In die Taskleiste einhaengen (bleibt so auch bei Startmenue/Flyouts sichtbar) ----
        public IntPtr Taskbar = IntPtr.Zero;
        public bool Embedded { get { return Taskbar != IntPtr.Zero; } }

        public bool TryEmbed()
        {
            try
            {
                IntPtr tb = Native.FindTaskbar(ScreenName);
                if (tb == IntPtr.Zero) return false;
                const int GWL_STYLE = -16;
                const int WS_CHILD = 0x40000000;
                const int WS_POPUP = unchecked((int)0x80000000);
                int style = Native.GetWindowLong(Handle, GWL_STYLE);
                Native.SetWindowLong(Handle, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
                IntPtr prev = Native.SetParent(Handle, tb);
                if (prev == IntPtr.Zero && Native.GetParent(Handle) != tb)
                {
                    Native.SetWindowLong(Handle, GWL_STYLE, style); // zurueck zum Rueckfall-Verfahren
                    return false;
                }
                Taskbar = tb;
                UpdatePosition();
                return true;
            }
            catch (Exception ex) { Program.LogError(ex); return false; }
        }

        // Taskleiste weg (z.B. Explorer neu gestartet) -> neu aufbauen
        public bool EmbedBroken { get { return Embedded && (!Native.IsWindow(Taskbar) || Native.GetParent(Handle) != Taskbar); } }

        int TileWidth { get { return (int)(ctx.Settings.TileWidth * ctx.Scale * cfg.Size / 100f); } }
        int Gap { get { return (int)(6 * ctx.Scale); } }

        // neue Konfiguration (Vorschau / Uebernehmen) ohne das Fenster neu anzulegen
        public void SetConfig(MonitorEntry config)
        {
            cfg = config;
            UpdatePosition();
            Invalidate();
        }

        // Taskleiste dieses Monitors (fuer automatisch ausblendende Taskleisten: folgt ihrer Position)
        public IntPtr TaskbarWnd = IntPtr.Zero;
        // Leiste liegt gerade unterhalb des Bildschirms (Taskleiste ausgeblendet)
        public bool OffScreen;

        public void UpdatePosition()
        {
            Screen s = Screen.AllScreens.FirstOrDefault(scr => scr.DeviceName == ScreenName);
            if (s == null) return;
            int tbH = s.Bounds.Bottom - s.WorkingArea.Bottom;
            int tbTop = s.Bounds.Bottom - tbH;
            // Die echte Lage der Taskleiste verwenden: bei "Taskleiste automatisch ausblenden" ist die
            // Arbeitsflaeche der ganze Bildschirm, die Taskleiste faehrt aber hoch und runter.
            Native.RECT tr;
            if (!IsOwnTaskbar(TaskbarWnd, s, out tr)) TaskbarWnd = FindOwnTaskbar(s, out tr);
            if (TaskbarWnd != IntPtr.Zero)
            {
                tbH = tr.Bottom - tr.Top;
                tbTop = tr.Top;
            }
            if (tbH <= 0) { tbH = (int)(48 * ctx.Scale); tbTop = s.Bounds.Bottom - tbH; }
            int pad = (int)(4 * ctx.Scale);
            int h = Math.Max(16, tbH - 2 * pad);   // Hoehe immer passend zur Taskleiste
            EnsureFonts(h / 40f);
            int n = cfg.Devices.Count;
            int w = Math.Max(1, n * TileWidth + (n - 1) * Gap);
            int x = s.Bounds.Left + (int)(8 * ctx.Scale);
            int y = tbTop + pad;
            // Maus auf den Kacheln: Windows zaehlt das nicht als "ueber der Taskleiste" und faehrt sie ein.
            // Die Kacheln bleiben dann stehen, bis die Maus sie verlaesst (danach gleiten sie hinterher).
            if (hovering && y > Top && !Embedded) { y = Top; }
            // komplett ausgeblendete Taskleiste: Kacheln nicht auf einem darunterliegenden Monitor zeigen
            OffScreen = y >= s.Bounds.Bottom;
            if (Embedded)
            {
                // Koordinaten relativ zur Taskleiste
                Native.RECT er;
                if (!Native.GetWindowRect(Taskbar, out er)) return;
                int rx = x - er.Left, ry = y - er.Top;
                bool sizeChanged = Width != w || Height != h;
                Native.SetWindowPos(Handle, IntPtr.Zero, rx, ry, w, h, 0x0010 | 0x0040); // NOACTIVATE | SHOWWINDOW, oben in der Taskleiste
                if (sizeChanged) Invalidate();
                return;
            }
            bool resized = Width != w || Height != h;
            if (gliding && !resized && Left == x && Top != y && !hovering)
            {
                // nach dem Verlassen: in wenigen Schritten zur Taskleiste gleiten statt zu springen
                glideTo = y;
                OffScreen = Top >= s.Bounds.Bottom; // erst ausblenden, wenn sie wirklich unten angekommen ist
                if (!glide.Enabled) glide.Start();
                return;
            }
            StopGlide();
            if (resized || Left != x || Top != y) SetBounds(x, y, w, h); // reines Verschieben braucht kein Neuzeichnen
            if (resized) Invalidate();
        }

        // Taskleiste des eigenen Monitors: unten, mindestens halb so breit, Oberkante auf diesem Monitor
        // (eine ausgeblendete Taskleiste ragt nach unten heraus - Screen.FromRectangle wuerde bei
        // uebereinander stehenden Monitoren den falschen liefern)
        static bool IsOwnTaskbar(IntPtr h, Screen s, out Native.RECT r)
        {
            r = new Native.RECT();
            if (h == IntPtr.Zero || !Native.GetWindowRect(h, out r)) return false;
            int overlap = Math.Min(r.Right, s.Bounds.Right) - Math.Max(r.Left, s.Bounds.Left);
            return r.Bottom > r.Top && overlap >= s.Bounds.Width / 2
                && r.Top >= s.Bounds.Top && r.Top < s.Bounds.Bottom && r.Bottom >= s.Bounds.Bottom - 2;
        }

        static IntPtr FindOwnTaskbar(Screen s, out Native.RECT r)
        {
            foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = Native.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                    if (IsOwnTaskbar(h, s, out r)) return h;
            }
            r = new Native.RECT();
            return IntPtr.Zero;
        }

        bool hovering, gliding;
        System.Windows.Forms.Timer glide; // laeuft nur kurz nach dem Verlassen der Kacheln
        int glideTo;

        void StopGlide()
        {
            gliding = false;
            if (glide != null) glide.Stop();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; StopGlide(); }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovering = false;
            if (glide == null)
            {
                glide = new System.Windows.Forms.Timer { Interval = 15 };
                glide.Tick += delegate
                {
                    if (IsDisposed) { StopGlide(); return; }
                    int d = glideTo - Top;
                    if (Math.Abs(d) <= 1) { StopGlide(); Top = glideTo; }
                    else Top += d > 0 ? Math.Max(1, d / 3) : Math.Min(-1, d / 3);
                    Screen s = Screen.AllScreens.FirstOrDefault(scr => scr.DeviceName == ScreenName);
                    if (s != null) OffScreen = Top >= s.Bounds.Bottom;
                    ctx.BarMoved(this);
                };
            }
            gliding = true;
            UpdatePosition();         // startet das Gleiten, falls die Taskleiste inzwischen woanders ist
            if (!glide.Enabled) gliding = false;
            ctx.BarMoved(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && glide != null) { glide.Stop(); glide.Dispose(); glide = null; }
            base.Dispose(disposing);
        }

        // sichtbar/unsichtbar, ohne zu aktivieren (Form.Show() wuerde den Fokus stehlen)
        public void SetShown(bool show)
        {
            if (show == Native.IsWindowVisible(Handle)) return;
            Native.ShowWindow(Handle, show ? 4 /*SW_SHOWNOACTIVATE*/ : 0 /*SW_HIDE*/);
        }

        static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = 2 * rad;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (sc <= 0) return;
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var devs = cfg.Devices;
            int tw = TileWidth, gap = Gap, h = ClientSize.Height;

            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var sfName = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var fgb = new SolidBrush(ctx.Fg))
            using (var barb = new SolidBrush(ctx.BarBg))
            {
                for (int i = 0; i < devs.Count; i++)
                {
                    var en = devs[i];
                    var r = new Rectangle(i * (tw + gap), 0, tw, h);
                    var info = ctx.Get(en.Id);
                    int? pct = ctx.DisplayPercent(en.Id);        // auch getrennt: letzter Wert
                    bool online = ctx.IsOnline(en.Id);
                    bool chg = online && info != null && info.Charging;

                    Color c;
                    if (!pct.HasValue) c = Color.Gray;
                    else if (!online && ctx.GrayOffline) c = Color.Gray;   // getrennt / Ruhemodus
                    else if (chg) c = ctx.ColorOf(Palette.Charging);
                    else c = ctx.LevelColor(pct.Value);
                    bool alarm = online && pct.HasValue && !chg && pct.Value <= ctx.WarnLevel;

                    g.SmoothingMode = SmoothingMode.None;
                    using (var path = RoundRect(r, (int)(6 * sc)))
                    using (var bb = new SolidBrush(alarm ? ctx.AlarmBg : ctx.TileBg))
                        g.FillPath(bb, path);

                    using (var cb = new SolidBrush(c))
                    {
                        g.DrawString(Icons.Glyph(en.Icon), fGlyph, cb, new RectangleF(r.X + 4 * sc, r.Y, 30 * sc, r.Height), sfCenter);
                        float tx = r.X + 38 * sc;
                        float textW = r.Right - tx - 4 * sc;
                        if (textW > 8)
                        {
                            g.DrawString(en.Label, fName, fgb, new RectangleF(tx, r.Y + 2 * sc, textW, 17 * sc), sfName);
                            string txt = pct.HasValue ? pct.Value + " %" : "--";
                            g.DrawString(txt, fPct, cb, new RectangleF(tx - 1 * sc, r.Y + 18 * sc, textW + 2 * sc, 20 * sc), sfName);
                            var sz = g.MeasureString(txt, fPct);
                            if (chg && tx + sz.Width + 6 * sc < r.Right)
                                g.DrawString("\uE945", fSmallGlyph, cb, tx + sz.Width - 5 * sc, r.Y + 22 * sc);
                            // Balken nur, wenn die Kachel breit genug ist
                            float bx = tx + 58 * sc, bw = r.Right - 10 * sc - bx, by = r.Y + 26 * sc, bh = 5 * sc;
                            if (bw >= 18 * sc)
                            {
                                g.FillRectangle(barb, bx, by, bw, bh);
                                if (pct.HasValue) g.FillRectangle(cb, bx, by, bw * Math.Min(100, pct.Value) / 100f, bh);
                            }
                        }
                    }
                }
            }
        }
    }

    // ======================= Alarm: eigenes Fenster + Ton =======================
    static class AlarmSound
    {
        static byte[] wav;
        static System.Media.SoundPlayer player;

        // 3x Doppel-Piepton als WAV im Speicher erzeugen (spielt auch bei "Nicht stoeren")
        static byte[] Build()
        {
            const int rate = 22050;
            var samples = new List<short>();
            for (int rep = 0; rep < 3; rep++)
            {
                foreach (var freq in new[] { 988.0, 784.0 })
                {
                    int n = rate * 160 / 1000;
                    for (int i = 0; i < n; i++)
                    {
                        double env = Math.Min(1.0, Math.Min(i, n - i) / (rate * 0.01)); // weiche Kanten
                        samples.Add((short)(Math.Sin(2 * Math.PI * freq * i / rate) * 26000 * env));
                    }
                    for (int i = 0; i < rate * 50 / 1000; i++) samples.Add(0);
                }
                for (int i = 0; i < rate * 250 / 1000; i++) samples.Add(0);
            }
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int dataLen = samples.Count * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
            foreach (var s in samples) w.Write(s);
            return ms.ToArray();
        }

        static System.Windows.Media.MediaPlayer mp;

        // Standard-Warnton als Datei (fuer den MediaPlayer, der die Lautstaerke regeln kann)
        static string DefaultFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige");
                string f = Path.Combine(dir, "alarm_standard_v2.wav");
                if (!File.Exists(f))
                {
                    Directory.CreateDirectory(dir);
                    if (wav == null) wav = Build();
                    File.WriteAllBytes(f, wav);
                }
                return f;
            }
        }

        // Spielt den Alarm mit eigener Lautstaerke (0-100) – gilt fuer jedes Ausgabegeraet,
        // weil die Lautstaerke im Ton selbst gesetzt wird. Eigene Datei: WAV, MP3, WMA, M4A ...
        public static void Play(string file, int volume)
        {
            try
            {
                string f = !string.IsNullOrEmpty(file) && File.Exists(file) ? file : DefaultFile;
                if (mp == null) mp = new System.Windows.Media.MediaPlayer();
                mp.Stop();
                mp.Close();
                mp.Volume = Math.Max(0, Math.Min(100, volume)) / 100.0;
                mp.Open(new Uri(f));
                mp.Play();
            }
            catch (Exception ex)
            {
                Program.LogError(ex);
                try
                {
                    if (wav == null) wav = Build();
                    if (player != null) { player.Stop(); player.Dispose(); }
                    player = new System.Media.SoundPlayer(new MemoryStream(wav));
                    player.Play();
                }
                catch { try { System.Media.SystemSounds.Exclamation.Play(); } catch { } }
            }
        }

        public static void Stop() { try { if (mp != null) mp.Stop(); } catch { } }
    }

    class AlarmForm : Form
    {
        System.Windows.Forms.Timer topTimer, closeTimer;

        public AlarmForm(AppCtx ctx, Screen s, string title, string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(36, 36, 36);
            Size = new Size(400, 128);
            var wa = s.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

            var accent = new Panel { Bounds = new Rectangle(0, 0, 6, 128), BackColor = Color.FromArgb(235, 50, 50) };
            var icon = new Label { Text = "", Font = new Font("Segoe Fluent Icons", 26f, FontStyle.Regular, GraphicsUnit.Pixel), ForeColor = Color.FromArgb(235, 50, 50), Bounds = new Rectangle(18, 18, 40, 40), TextAlign = ContentAlignment.MiddleCenter };
            var lblT = new Label { Text = title, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Color.White, Bounds = new Rectangle(66, 14, 320, 26), AutoEllipsis = true };
            var lblX = new Label { Text = text, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(220, 220, 220), Bounds = new Rectangle(66, 42, 320, 44), AutoEllipsis = true };
            var btn = new Button { Text = "OK", Bounds = new Rectangle(296, 88, 90, 28), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60, 60, 60) };
            btn.FlatAppearance.BorderColor = Color.FromArgb(110, 110, 110);
            btn.Click += delegate { AlarmSound.Stop(); Close(); }; // OK beendet auch einen langen eigenen Sound
            Controls.AddRange(new Control[] { accent, icon, lblT, lblX, btn });

            // immer im Vordergrund halten (auch ueber Spielen im randlosen Vollbild)
            topTimer = new System.Windows.Forms.Timer { Interval = 500 };
            topTimer.Tick += delegate { if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); };
            topTimer.Start();
            // nach 10 Minuten automatisch schliessen
            closeTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            closeTimer.Tick += delegate { Close(); };
            closeTimer.Start();
            FormClosed += delegate { topTimer.Stop(); closeTimer.Stop(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } } // nimmt dem Spiel nicht den Fokus

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }
    }

    // ======================= Design (hell / dunkel) =======================
    static class Theme
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string idList);

        public static bool SystemAppsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; }
        }

        public static Color Back(bool d) { return d ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); }
        public static Color Page(bool d) { return d ? Color.FromArgb(43, 43, 43) : Color.White; }
        public static Color Fore(bool d) { return d ? Color.FromArgb(235, 235, 235) : Color.FromArgb(25, 25, 25); }
        public static Color Dim(bool d) { return d ? Color.FromArgb(150, 150, 150) : Color.DimGray; }
        public static Color Input(bool d) { return d ? Color.FromArgb(56, 56, 56) : Color.White; }
        public static Color Border(bool d) { return d ? Color.FromArgb(85, 85, 85) : Color.FromArgb(200, 200, 200); }
        public static Color Accent(bool d) { return d ? Color.FromArgb(76, 160, 255) : Color.FromArgb(0, 103, 192); }
        public static Color HeaderBack(bool d) { return d ? Color.FromArgb(50, 50, 50) : Color.FromArgb(240, 240, 240); }

        public static void Apply(Control root, bool d)
        {
            var f = root as Form;
            if (f != null)
            {
                f.BackColor = Back(d); f.ForeColor = Fore(d);
                try { int v = d ? 1 : 0; DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { } // dunkle Titelleiste
            }
            ApplyRec(root, d, Back(d));
        }

        static void ApplyRec(Control c, bool d, Color bg)
        {
            foreach (Control ch in c.Controls)
            {
                Color childBg = bg;
                if (ch is SimpleTabs) { ((SimpleTabs)ch).SetColors(d); childBg = Page(d); }
                else if (ch is MonitorPage || (ch is Panel && ch.Parent != null && ch.Parent.Parent is SimpleTabs))
                {
                    ch.BackColor = Page(d); ch.ForeColor = Fore(d); childBg = Page(d);
                    if (((ScrollableControl)ch).AutoScroll) // scrollbare Seite: Bildlaufleiste im passenden Design
                    {
                        listDark[ch] = d;
                        if (ch.IsHandleCreated) ApplyListTheme(ch);
                        else if (listHooked.Add(ch)) ch.HandleCreated += (s, e) => ApplyListTheme((Control)s);
                    }
                }
                else if (ch is GroupBox) { ch.ForeColor = Fore(d); }
                else if (ch is Label)
                {
                    ((Label)ch).UseMnemonic = false; // "&" als Zeichen anzeigen (z. B. "Bobohatt & Diabeetus")
                    if (ch.Tag == null) ch.Tag = ch.ForeColor == Color.DimGray ? "dim" : "fg";
                    string t = ch.Tag as string;
                    if (t == "dim") ch.ForeColor = Dim(d);
                    else if (t == "fg") ch.ForeColor = Fore(d);
                }
                else if (ch is Button)
                {
                    var b = (Button)ch;
                    if (d) { b.FlatStyle = FlatStyle.Flat; b.BackColor = Input(d); b.ForeColor = Fore(d); b.FlatAppearance.BorderColor = Border(d); }
                    else { b.FlatStyle = FlatStyle.Standard; b.UseVisualStyleBackColor = true; b.ForeColor = SystemColors.ControlText; }
                }
                else if (ch is CheckBox) { ch.ForeColor = Fore(d); }
                else if (ch is ComboBox)
                {
                    var cb = (ComboBox)ch;
                    cb.FlatStyle = d ? FlatStyle.Flat : FlatStyle.Standard;
                    cb.BackColor = d ? Input(d) : SystemColors.Window;
                    cb.ForeColor = d ? Fore(d) : SystemColors.WindowText;
                    comboDark[cb] = d;
                    if (comboHooked.Add(cb)) OwnerDrawCombo(cb);
                    cb.Invalidate();
                }
                else if (ch is NumericUpDown) { ch.BackColor = d ? Input(d) : SystemColors.Window; ch.ForeColor = d ? Fore(d) : SystemColors.WindowText; }
                else if (ch is TextBox)
                {
                    ch.BackColor = d ? Input(d) : SystemColors.Window; ch.ForeColor = d ? Fore(d) : SystemColors.WindowText;
                    if (((TextBox)ch).Multiline) // Bildlaufleiste im passenden Design
                    {
                        listDark[ch] = d;
                        if (ch.IsHandleCreated) ApplyListTheme(ch);
                        else if (listHooked.Add(ch)) ch.HandleCreated += (s, e) => ApplyListTheme((Control)s);
                    }
                }
                else if (ch is ListView)
                {
                    ch.BackColor = d ? Color.FromArgb(38, 38, 38) : SystemColors.Window;
                    ch.ForeColor = d ? Fore(d) : SystemColors.WindowText;
                    // Scrollleisten-Design erst, wenn die Liste wirklich angelegt ist – ein vorzeitiger Zugriff auf
                    // .Handle bei versteckten Reitern fuehrt sonst dazu, dass die Liste nie gezeichnet wird.
                    listDark[ch] = d;
                    if (ch.IsHandleCreated) ApplyListTheme(ch);
                    else if (!listHooked.Contains(ch)) { listHooked.Add(ch); ch.HandleCreated += (s, e) => ApplyListTheme((Control)s); }
                    ch.Invalidate();
                }
                else if (ch is TrackBar || ch is ColorRangeBar) { ch.BackColor = bg; ch.ForeColor = Dim(d); }
                ApplyRec(ch, d, childBg);
            }
        }

        // Auswahlfelder selbst zeichnen: Windows malt ausgegraute Felder sonst immer hell mit schwarzer Schrift
        static readonly Dictionary<ComboBox, bool> comboDark = new Dictionary<ComboBox, bool>();
        static readonly HashSet<ComboBox> comboHooked = new HashSet<ComboBox>();
        static void OwnerDrawCombo(ComboBox cb)
        {
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.EnabledChanged += (s, e) => cb.Invalidate();
            cb.DrawItem += (s, e) =>
            {
                bool d;
                comboDark.TryGetValue(cb, out d);
                bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                Color bg, fg;
                if (!cb.Enabled) { bg = d ? Color.FromArgb(45, 45, 45) : SystemColors.Control; fg = d ? Fore(d) : SystemColors.GrayText; } // dunkel: weiss wie alle Texte
                else if (sel) { bg = d ? Color.FromArgb(0, 84, 153) : SystemColors.Highlight; fg = d ? Color.White : SystemColors.HighlightText; }
                else { bg = d ? Input(d) : SystemColors.Window; fg = d ? Fore(d) : SystemColors.WindowText; }
                using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                string text = e.Index >= 0 ? cb.GetItemText(cb.Items[e.Index]) : "";
                TextRenderer.DrawText(e.Graphics, text, cb.Font, new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            };
        }

        public static Color BorderFor(Control c) { return Border(c.BackColor.GetBrightness() < 0.5f || (c.Parent != null && c.Parent.BackColor.GetBrightness() < 0.5f)); }

        static readonly Dictionary<Control, bool> listDark = new Dictionary<Control, bool>();
        static readonly HashSet<Control> listHooked = new HashSet<Control>();
        static void ApplyListTheme(Control c)
        {
            bool d;
            if (!listDark.TryGetValue(c, out d)) return;
            try { SetWindowTheme(c.Handle, d ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        // Spaltenkoepfe selbst zeichnen (sonst bleiben sie im dunklen Modus hell)
        public static void OwnerDrawHeader(ListView lv)
        {
            lv.OwnerDraw = true;
            // letzte Spalte bis zum rechten Rand – sonst bleibt rechts im Spaltenkopf ein weisses Kaestchen
            EventHandler fill = (s, e) =>
            {
                if (lv.Columns.Count == 0 || !lv.IsHandleCreated) return;
                int used = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++) used += lv.Columns[i].Width;
                int w = lv.ClientSize.Width - used;
                var last = lv.Columns[lv.Columns.Count - 1];
                if (w > 40 && last.Width != w) last.Width = w;
            };
            lv.HandleCreated += fill;
            lv.Resize += fill;
            lv.Layout += (s, e) => fill(s, e);
            lv.DrawItem += (s, e) => { };
            // Zeilen selbst zeichnen: Schrift bleibt auch bei markierter Zeile gut lesbar (dunkel: weiss)
            lv.DrawSubItem += (s, e) =>
            {
                bool d = lv.BackColor.GetBrightness() < 0.5f;
                bool sel = e.Item.Selected;
                var g = e.Graphics;
                Rectangle b = e.Bounds;
                if (e.ColumnIndex == 0) b.Width = lv.Columns[0].Width; // Spalte 0 meldet sonst die ganze Zeile
                Color bg = sel ? (d ? Color.FromArgb(0, 84, 153) : Color.FromArgb(204, 228, 247)) : lv.BackColor;
                Color fg = d ? Color.White : Color.FromArgb(20, 20, 20);
                using (var bb = new SolidBrush(bg)) g.FillRectangle(bb, b);
                var tr = new Rectangle(b.X + 6, b.Y, b.Width - 8, b.Height);
                if (e.ColumnIndex == 0 && lv.CheckBoxes)
                {
                    var glyph = e.Item.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                                               : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
                    var sz = CheckBoxRenderer.GetGlyphSize(g, glyph);
                    CheckBoxRenderer.DrawCheckBox(g, new Point(b.X + 4, b.Y + (b.Height - sz.Height) / 2), glyph);
                    tr = new Rectangle(b.X + sz.Width + 9, b.Y, b.Width - sz.Width - 11, b.Height);
                }
                TextRenderer.DrawText(g, e.SubItem.Text, lv.Font, tr, fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            };
            lv.DrawColumnHeader += (s, e) =>
            {
                bool d = lv.BackColor.GetBrightness() < 0.5f;
                using (var bb = new SolidBrush(HeaderBack(d))) e.Graphics.FillRectangle(bb, e.Bounds);
                using (var pen = new Pen(Border(d)))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                }
                var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, Fore(d), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
    }

    // ======================= Scrollbare Seite ohne Zeichenfehler =======================
    class ScrollPage : Panel
    {
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 60 };
        int lastY;

        public ScrollPage()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            // jede Aenderung der Scrollposition (Rad, Leiste, Tastatur, Fokus) -> komplett neu zeichnen
            watch.Tick += delegate
            {
                int y = AutoScrollPosition.Y;
                if (y != lastY) { lastY = y; Invalidate(true); Update(); }
            };
            VisibleChanged += delegate { if (Visible) watch.Start(); else watch.Stop(); };
            Disposed += delegate { watch.Dispose(); };
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0115 /*WM_VSCROLL*/ || m.Msg == 0x020A /*WM_MOUSEWHEEL*/) { lastY = AutoScrollPosition.Y; Invalidate(true); }
        }

        // ganze Seite samt Inhalt in einem Durchgang zeichnen (keine verschobenen Bildreste beim Scrollen)
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; /*WS_EX_COMPOSITED*/ return cp; }
        }

        // Mausrad ueber Auswahlfeldern/Reglern scrollt die Seite statt den Wert zu verstellen
        // (ausser das Feld wurde angeklickt bzw. die Auswahlliste ist aufgeklappt)
        protected override void OnControlAdded(ControlEventArgs e) { base.OnControlAdded(e); HookWheel(e.Control); }

        void HookWheel(Control c)
        {
            if (c is ComboBox || c is TrackBar || c is NumericUpDown)
                c.MouseWheel += (s, e) =>
                {
                    var cb = s as ComboBox;
                    if ((cb != null && cb.DroppedDown) || ((Control)s).Focused) return;
                    var he = e as HandledMouseEventArgs;
                    if (he != null) he.Handled = true;
                    ScrollBy(-e.Delta);
                };
            foreach (Control ch in c.Controls) HookWheel(ch);
            c.ControlAdded += (s, e) => HookWheel(e.Control);
        }

        void ScrollBy(int delta)
        {
            int y = -AutoScrollPosition.Y + delta * SystemInformation.MouseWheelScrollLines * 16 / 120;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
            lastY = AutoScrollPosition.Y;
            Invalidate(true);
        }
    }

    // ======================= Gruppenrahmen (selbst gezeichnet) =======================
    // Der Windows-Rahmen ist im dunklen Design hellgrau und bekommt beim Scrollen Luecken.
    class ThemedGroupBox : GroupBox
    {
        public ThemedGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        // GroupBox ist in Windows "durchsichtig" (WS_EX_TRANSPARENT) -> beim Scrollen bleiben Bildreste stehen
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle &= ~0x20; return cp; }
        }

        // Elemente, die ueber den rechten Rand ragen, wuerden den Rahmen uebermalen -> kuerzen
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int max = Width - 3;
            foreach (Control c in Controls)
                if (c.Right > max && c.Left < max) c.Width = Math.Max(10, max - c.Left);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
            var ts = TextRenderer.MeasureText(Text, Font);
            int top = ts.Height / 2;
            using (var pen = new Pen(Theme.BorderFor(this)))
            {
                var r = new Rectangle(0, top, Width - 1, Height - top - 1);
                g.DrawRectangle(pen, r);
            }
            if (!string.IsNullOrEmpty(Text))
            {
                var tr = new Rectangle(8, 0, ts.Width + 2, ts.Height);
                using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, tr);
                TextRenderer.DrawText(g, Text, Font, tr, ForeColor, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }
    }

    // ======================= Eigene Reiter (lassen sich hell/dunkel faerben) =======================
    class SimpleTabs : Control
    {
        public List<Control> Pages = new List<Control>();
        Panel body;
        int sel = -1;
        public int SelectedIndex { get { return sel; } }
        List<Rectangle> hits = new List<Rectangle>();
        const int HeaderH = 32;
        Color headerBack = Color.FromArgb(243, 243, 243), pageBack = Color.White, text = Color.Black,
              dim = Color.DimGray, accent = Color.FromArgb(0, 103, 192), line = Color.FromArgb(200, 200, 200);

        public SimpleTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            body = new Panel();
            Controls.Add(body);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (body != null) body.Bounds = new Rectangle(1, HeaderH, Math.Max(0, Width - 2), Math.Max(0, Height - HeaderH - 1));
        }

        public void AddPage(Control p)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            body.Controls.Add(p);
            Pages.Add(p);
            if (sel < 0) SelectIndex(0);
            Invalidate();
        }

        public void SelectPage(Control p) { SelectIndex(Pages.IndexOf(p)); }
        public void Next(int dir) { if (Pages.Count > 0) SelectIndex((sel + dir + Pages.Count) % Pages.Count); }

        public void SelectIndex(int i)
        {
            if (i < 0 || i >= Pages.Count) return;
            sel = i;
            for (int k = 0; k < Pages.Count; k++) Pages[k].Visible = k == i;
            Invalidate();
        }

        public void SetColors(bool d)
        {
            headerBack = Theme.Back(d); pageBack = Theme.Page(d); text = Theme.Fore(d);
            dim = Theme.Dim(d); accent = Theme.Accent(d); line = Theme.Border(d);
            BackColor = headerBack;
            body.BackColor = pageBack;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(headerBack);
            hits.Clear();
            int x = 0;
            Rectangle selRect = Rectangle.Empty;
            using (var fSel = new Font(Font, FontStyle.Bold))
            {
                for (int i = 0; i < Pages.Count; i++)
                {
                    var f = i == sel ? fSel : Font;
                    int w = TextRenderer.MeasureText(Pages[i].Text, fSel).Width + 22;
                    var r = new Rectangle(x, 2, w, HeaderH - 2);
                    hits.Add(r);
                    if (i == sel)
                    {
                        selRect = r;
                        using (var b = new SolidBrush(pageBack)) g.FillRectangle(b, r.X, r.Y, r.Width, r.Height + 1);
                        using (var p = new Pen(line)) { g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom); g.DrawLine(p, r.Right, r.Top, r.Right, r.Bottom); }
                        using (var b = new SolidBrush(accent)) g.FillRectangle(b, r.X, r.Y, r.Width + 1, 3);
                    }
                    TextRenderer.DrawText(g, Pages[i].Text, f, r, i == sel ? text : dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += w + 2;
                }
            }
            using (var p = new Pen(line))
            {
                // Linie unter den Reitern (ausser unter dem gewaehlten) + Rahmen um den Inhalt
                g.DrawLine(p, 0, HeaderH - 1, selRect.IsEmpty ? Width : selRect.Left, HeaderH - 1);
                if (!selRect.IsEmpty) g.DrawLine(p, selRect.Right, HeaderH - 1, Width, HeaderH - 1);
                g.DrawLine(p, 0, HeaderH - 1, 0, Height - 1);
                g.DrawLine(p, Width - 1, HeaderH - 1, Width - 1, Height - 1);
                g.DrawLine(p, 0, Height - 1, Width - 1, Height - 1);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < hits.Count; i++) if (hits[i].Contains(e.Location)) { SelectIndex(i); break; }
        }
    }

    // ======================= Farben =======================
    static class Palette
    {
        public const int Red = 0, Orange = 1, Green = 2, Charging = 3;
        public static readonly Color[] Default =
        {
            Color.FromArgb(235, 50, 50), Color.FromArgb(255, 140, 0), Color.FromArgb(60, 190, 90), Color.FromArgb(0, 150, 255)
        };
    }

    // Windows-Farbauswahl mit eigenem Titel und zusaetzlichem Button "Standardfarbe" unten im Dialog
    class ColorPicker : ColorDialog
    {
        public string Title, ResetText;
        public bool ResetRequested;
        const int WM_INITDIALOG = 0x0110, WM_COMMAND = 0x0111, WM_GETFONT = 0x0031, WM_SETFONT = 0x0030, ID_RESET = 0x7A11;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(int ex, string cls, string text, int style, int x, int y, int w, int h, IntPtr parent, IntPtr id, IntPtr inst, IntPtr param);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.RECT r);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool SetWindowText(IntPtr h, string t);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dlg, int id);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool MapWindowPoints(IntPtr from, IntPtr to, ref Native.RECT r, int n);

        protected override IntPtr HookProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam)
        {
            if (msg == WM_INITDIALOG)
            {
                var res = base.HookProc(hWnd, msg, wparam, lparam);
                try
                {
                    if (!string.IsNullOrEmpty(Title)) SetWindowText(hWnd, Title);
                    // Fenster um eine Zeile verlaengern und den Button unter OK/Abbrechen setzen
                    Native.RECT cr, wr, ok;
                    GetClientRect(hWnd, out cr);
                    Native.GetWindowRect(hWnd, out wr);
                    IntPtr hOk = GetDlgItem(hWnd, 1);
                    int bh = 26, margin = 8;
                    if (hOk != IntPtr.Zero && Native.GetWindowRect(hOk, out ok)) { MapWindowPoints(IntPtr.Zero, hWnd, ref ok, 2); bh = ok.Bottom - ok.Top; margin = Math.Max(6, ok.Left); }
                    Native.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, wr.Right - wr.Left, wr.Bottom - wr.Top + bh + margin, 0x0002 | 0x0004 | 0x0010); // NOMOVE|NOZORDER|NOACTIVATE
                    const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000;
                    IntPtr btn = CreateWindowEx(0, "BUTTON", ResetText ?? "Standard", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
                                                margin, cr.Bottom, cr.Right - 2 * margin, bh, hWnd, new IntPtr(ID_RESET), IntPtr.Zero, IntPtr.Zero);
                    SendMessage(btn, WM_SETFONT, SendMessage(hWnd, WM_GETFONT, IntPtr.Zero, IntPtr.Zero), new IntPtr(1));
                }
                catch (Exception ex) { Program.LogError(ex); }
                return res;
            }
            if (msg == WM_COMMAND && ((int)wparam & 0xFFFF) == ID_RESET)
            {
                ResetRequested = true;
                PostMessage(hWnd, WM_COMMAND, new IntPtr(2) /*IDCANCEL*/, IntPtr.Zero);
                return new IntPtr(1);
            }
            return base.HookProc(hWnd, msg, wparam, lparam);
        }
    }

    // ======================= Farbbereichs-Linie (Rot | Orange | Gruen) =======================
    class ColorRangeBar : Control
    {
        int orange = 30, green = 70, drag;
        public event EventHandler ValueChanged;
        Color[] colors = { Palette.Default[Palette.Red], Palette.Default[Palette.Orange], Palette.Default[Palette.Green] };
        public void SetColors(Color red, Color orangeCol, Color greenCol) { colors = new[] { red, orangeCol, greenCol }; Invalidate(); }

        public ColorRangeBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public int OrangeFrom { get { return orange; } set { orange = Math.Max(1, Math.Min(99, value)); if (green <= orange) green = orange + 1; Invalidate(); } }
        public int GreenFrom { get { return green; } set { green = Math.Max(2, Math.Min(100, value)); if (orange >= green) orange = green - 1; Invalidate(); } }

        RectangleF Track { get { return new RectangleF(12, 12, Width - 24, 10); } }
        float X(int v) { var t = Track; return t.X + t.Width * v / 100f; }
        int V(float x) { var t = Track; return (int)Math.Round((x - t.X) / t.Width * 100f); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = Track;
            using (var br = new SolidBrush(colors[0])) g.FillRectangle(br, X(0), t.Y, X(orange) - X(0), t.Height);
            using (var br = new SolidBrush(colors[1])) g.FillRectangle(br, X(orange), t.Y, X(green) - X(orange), t.Height);
            using (var br = new SolidBrush(colors[2])) g.FillRectangle(br, X(green), t.Y, X(100) - X(green), t.Height);
            using (var pen = new Pen(Color.Silver))
            using (var f = new Font("Segoe UI", 8f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
            {
                for (int v = 0; v <= 100; v += 10)
                {
                    g.DrawLine(pen, X(v), t.Bottom + 3, X(v), t.Bottom + (v % 50 == 0 ? 9 : 6));
                    if (v % 25 == 0 || v == 0) g.DrawString(v + " %", f, Brushes.Gray, X(v), t.Bottom + 10, sf);
                }
            }
            foreach (var v in new[] { orange, green })
            {
                float cx = X(v), cy = t.Y + t.Height / 2;
                g.FillEllipse(Brushes.White, cx - 8, cy - 8, 16, 16);
                using (var pen = new Pen(Color.FromArgb(70, 70, 70), 2)) g.DrawEllipse(pen, cx - 8, cy - 8, 16, 16);
            }
        }

        void SetGrip(int which, int v)
        {
            if (which == 1) v = Math.Max(1, Math.Min(green - 1, v));
            else v = Math.Max(orange + 1, Math.Min(100, v));
            if ((which == 1 ? orange : green) == v) return;
            if (which == 1) orange = v; else green = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            float d1 = Math.Abs(e.X - X(orange)), d2 = Math.Abs(e.X - X(green));
            drag = d1 < d2 || (d1 == d2 && e.X < X(orange)) ? 1 : 2;
            SetGrip(drag, V(e.X));
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != 0) SetGrip(drag, V(e.X));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = 0;
            Capture = false;
        }
    }

    // ======================= Einstellungsfenster =======================
    class Row { public string Id; public string Label; public string Icon; public bool Checked; public DeviceInfo Info; }

    // Ein Reiter je Monitor mit komplett eigener Konfiguration
    class MonitorPage : Panel
    {
        SettingsForm owner;
        AppCtx app;
        public MonitorEntry Entry;
        public string GdiName;
        public int Num;
        List<Row> rows = new List<Row>();
        bool loading;
        CheckBox chkEn;
        TrackBar trkSize;
        Label lblSize;
        ListView lv;
        TextBox txtLabel;
        ComboBox cmbIcon;

        public MonitorPage(SettingsForm o, int num, MonitorEntry entry, MonitorInfo info, AppCtx ctx)
        {
            owner = o; Num = num; Entry = entry; app = ctx;
            GdiName = info != null && info.Active ? info.GdiName : null;
            bool connected = info != null;
            Text = num + ". " + entry.Name + (GdiName == null ? L.T(" (aus)") : "");

            string state;
            Screen s = GdiName == null ? null : Screen.AllScreens.FirstOrDefault(x => x.DeviceName == GdiName);
            if (s != null) state = entry.Name + "  ·  " + s.Bounds.Width + " × " + s.Bounds.Height + (s.Primary ? "  ·  " + L.T("Hauptbildschirm") : "");
            else if (connected) state = entry.Name + "  ·  " + L.T("deaktiviert – die Leiste erscheint automatisch, sobald er eingeschaltet wird");
            else state = entry.Name + "  ·  " + L.T("nicht verbunden – die Leiste erscheint automatisch, sobald er angeschlossen wird");
            var lblState = new Label { Text = state, Bounds = new Rectangle(12, 12, 610, 20), Font = new Font("Segoe UI Semibold", 9f) };

            chkEn = new CheckBox { Text = L.T("Akku-Leiste auf diesem Monitor anzeigen"), Bounds = new Rectangle(12, 38, 400, 24), Checked = entry.Enabled };
            lblSize = new Label { Bounds = new Rectangle(12, 72, 600, 18) };
            trkSize = new TrackBar { Minimum = 50, Maximum = 200, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Bounds = new Rectangle(12, 90, 430, 45), Value = Math.Max(50, Math.Min(200, entry.Size)) };
            var lblSizeHint = new Label { Text = L.T("Die Höhe passt sich immer der Taskleiste an – der Regler ändert nur die Breite."), Bounds = new Rectangle(12, 134, 610, 18), ForeColor = Color.DimGray };

            var gDev = new ThemedGroupBox { Text = L.T("Geräte auf diesem Monitor"), Bounds = new Rectangle(8, 160, 624, 376) };
            var lblHint = new Label { Text = L.T("Angehakte Geräte werden als Kachel angezeigt – in der Reihenfolge der Liste (Zeilen mit der Maus ziehen)."), Bounds = new Rectangle(12, 22, 600, 18) };
            lv = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, Bounds = new Rectangle(12, 44, 480, 280) };
            lv.Columns.Add(L.T("Anzeigename"), 130);
            lv.Columns.Add(L.T("Gerät"), 120);
            lv.Columns.Add(L.T("Verbindung"), 112);
            lv.Columns.Add(L.T("Akku"), 98);
            Theme.OwnerDrawHeader(lv);
            var btnUp = new Button { Text = L.T("▲  Nach oben"), Bounds = new Rectangle(502, 44, 110, 32) };
            var btnDown = new Button { Text = L.T("▼  Nach unten"), Bounds = new Rectangle(502, 82, 110, 32) };
            var btnScan = new Button { Text = L.T("Geräte neu suchen"), Bounds = new Rectangle(502, 134, 110, 44) };
            var lblName = new Label { Text = L.T("Anzeigename:"), Bounds = new Rectangle(12, 340, 90, 20) };
            txtLabel = new TextBox { Bounds = new Rectangle(102, 337, 210, 24), Enabled = false };
            var lblIcon = new Label { Text = L.T("Symbol:"), Bounds = new Rectangle(328, 340, 56, 20) };
            cmbIcon = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(384, 337, 108, 24), Enabled = false };
            foreach (var n in Icons.Names) cmbIcon.Items.Add(new IconItem(n)); // Anzeige uebersetzt, gespeichert wird der deutsche Schluessel
            gDev.Controls.AddRange(new Control[] { lblHint, lv, btnUp, btnDown, btnScan, lblName, txtLabel, lblIcon, cmbIcon });

            Controls.AddRange(new Control[] { lblState, chkEn, lblSize, trkSize, lblSizeHint, gDev });

            foreach (var d in entry.Devices)
                rows.Add(new Row { Id = d.Id, Label = d.Label, Icon = d.Icon, Checked = true, Info = ctx.Get(d.Id) });
            if (AddNewRows(ctx.Available)) Sync();
            FillList(-1);
            UpdateSizeLabel();

            chkEn.CheckedChanged += delegate { Sync(); };
            trkSize.ValueChanged += delegate { UpdateSizeLabel(); Sync(); };
            btnUp.Click += delegate { MoveRow(-1); };
            btnDown.Click += delegate { MoveRow(1); };
            btnScan.Click += delegate { ctx.Poll(); };
            lv.ItemChecked += (snd, e) =>
            {
                if (loading || e.Item.Index >= rows.Count) return;
                rows[e.Item.Index].Checked = e.Item.Checked;
                Sync();
            };
            lv.SelectedIndexChanged += delegate { OnSelect(); };

            // Reihenfolge mit der Maus aendern (Zeile ziehen und loslassen)
            lv.AllowDrop = true;
            lv.ItemDrag += (snd, e) => { lv.DoDragDrop(e.Item, DragDropEffects.Move); };
            lv.DragEnter += (snd, e) => { e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None; };
            lv.DragOver += (snd, e) =>
            {
                if (!e.Data.GetDataPresent(typeof(ListViewItem))) { e.Effect = DragDropEffects.None; return; }
                e.Effect = DragDropEffects.Move;
                var pt = lv.PointToClient(new Point(e.X, e.Y));
                var over = lv.GetItemAt(8, pt.Y);
                if (over != null && !over.Selected) { loading = true; over.Selected = true; over.Focused = true; loading = false; } // Zielzeile markieren
            };
            lv.DragDrop += (snd, e) =>
            {
                var it = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
                if (it == null) return;
                int from = it.Index;
                var pt = lv.PointToClient(new Point(e.X, e.Y));
                var target = lv.GetItemAt(8, pt.Y);
                int to = target != null ? target.Index : rows.Count - 1;
                if (from < 0 || to < 0 || from == to) { FillList(from); return; }
                var r = rows[from];
                rows.RemoveAt(from);
                rows.Insert(to, r);
                FillList(to);
                Sync();
            };

            txtLabel.TextChanged += delegate
            {
                if (loading || lv.SelectedIndices.Count == 0) return;
                int i = lv.SelectedIndices[0];
                rows[i].Label = txtLabel.Text;
                lv.Items[i].Text = txtLabel.Text;
                Sync();
            };
            cmbIcon.SelectedIndexChanged += delegate
            {
                if (loading || lv.SelectedIndices.Count == 0 || cmbIcon.SelectedItem == null) return;
                rows[lv.SelectedIndices[0]].Icon = ((IconItem)cmbIcon.SelectedItem).Key;
                Sync();
            };
        }

        void UpdateSizeLabel() { lblSize.Text = L.F("Breite der Kacheln: {0} %", trkSize.Value); }

        class IconItem
        {
            public string Key;
            public IconItem(string k) { Key = k; }
            public override string ToString() { return L.T(Key); }
        }

        // Bedienelemente -> Entry uebertragen und Vorschau anstossen
        void Sync()
        {
            Entry.Enabled = chkEn.Checked;
            Entry.Size = trkSize.Value;
            Entry.Devices = rows.Where(r => r.Checked).Select(r => new DeviceEntry
            {
                Id = r.Id,
                Label = string.IsNullOrWhiteSpace(r.Label) ? (r.Info != null ? r.Info.Name : r.Id) : r.Label.Trim(),
                Icon = r.Icon ?? "Bluetooth"
            }).ToList();
            owner.Changed();
        }

        // true = mindestens ein neues Geraet wurde gleich angehakt
        bool AddNewRows(List<DeviceInfo> list)
        {
            // solange der Nutzer noch nie selbst ausgewaehlt hat: gefundene Geraete auf aktiven Leisten anhaken
            bool autoCheck = !app.Settings.DevicesConfigured && chkEn != null && chkEn.Checked;
            bool any = false;
            foreach (var d in list)
                if (!rows.Any(r => r.Id == d.Id))
                {
                    rows.Add(new Row { Id = d.Id, Label = d.Name, Icon = d.DefaultIcon, Checked = autoCheck, Info = d });
                    any |= autoCheck;
                }
            return any;
        }

        static string[] SubTexts(Row r)
        {
            if (r.Info == null)
            {
                string src = r.Id.StartsWith("logi:") ? "Logitech" : r.Id.StartsWith("hyperx:") ? "HyperX" : r.Id.StartsWith("ds:") ? "DualSense" : "Bluetooth";
                return new[] { r.Id.StartsWith("logi:") ? L.T("(G HUB?)") : L.T("(nicht gefunden)"), src, "--" };
            }
            string off = r.Id.StartsWith("logi:") ? L.T("Ruhe/aus") : L.T("getrennt");
            string akku = !r.Info.Percent.HasValue ? off
                        : !r.Info.Connected ? off + " (" + r.Info.Percent.Value + " %)"
                        : r.Info.Percent.Value + " %" + (r.Info.Charging ? L.T(" (lädt)") : "");
            return new[] { r.Info.Name, r.Info.Source, akku };
        }

        void FillList(int select)
        {
            loading = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var r in rows)
            {
                var it = new ListViewItem(r.Label);
                it.SubItems.AddRange(SubTexts(r));
                it.Checked = r.Checked;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            loading = false;
            if (select >= 0 && select < lv.Items.Count)
            {
                lv.Items[select].Selected = true;
                lv.Items[select].EnsureVisible();
            }
            OnSelect();
        }

        public void UpdateDevices(List<DeviceInfo> list)
        {
            foreach (var r in rows) r.Info = list.FirstOrDefault(d => d.Id == r.Id);
            int before = rows.Count;
            bool checkedNew = AddNewRows(list);
            if (rows.Count != before || lv.Items.Count != rows.Count)
            {
                FillList(lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] : -1);
                if (checkedNew) Sync();
                return;
            }
            lv.BeginUpdate();
            for (int i = 0; i < rows.Count; i++)
            {
                var st = SubTexts(rows[i]);
                for (int k = 0; k < st.Length; k++) lv.Items[i].SubItems[k + 1].Text = st[k];
            }
            lv.EndUpdate();
        }

        void OnSelect()
        {
            loading = true;
            if (lv.SelectedIndices.Count == 0)
            {
                txtLabel.Text = ""; txtLabel.Enabled = false;
                cmbIcon.SelectedIndex = -1; cmbIcon.Enabled = false;
            }
            else
            {
                var r = rows[lv.SelectedIndices[0]];
                txtLabel.Enabled = true; cmbIcon.Enabled = true;
                txtLabel.Text = r.Label;
                string key = Icons.Names.Contains(r.Icon) ? r.Icon : "Bluetooth";
                cmbIcon.SelectedItem = cmbIcon.Items.Cast<IconItem>().FirstOrDefault(x => x.Key == key);
            }
            loading = false;
        }

        void MoveRow(int dir)
        {
            if (lv.SelectedIndices.Count == 0) return;
            int i = lv.SelectedIndices[0], j = i + dir;
            if (j < 0 || j >= rows.Count) return;
            var t = rows[i]; rows[i] = rows[j]; rows[j] = t;
            FillList(j);
            lv.Focus();
            Sync();
        }
    }

    class SettingsForm : Form
    {
        AppCtx ctx;
        SimpleTabs tabs;
        List<MonitorPage> pages = new List<MonitorPage>();
        TrackBar trk;
        Label lblTrk;
        TrackBar trkVol;
        Label lblVol;
        ComboBox cmbSound;
        string soundFile;
        CheckBox chkAuto, chkMin, chkGray, chkThemeAuto, chkIdle;
        NumericUpDown numIdle;
        ComboBox cmbClose, cmbTheme, cmbLang;
        List<CheckBox> srcChecks = new List<CheckBox>();
        Label lblThemeMan;
        ColorRangeBar colBar;
        Label lblRed, lblOrange, lblGreen;
        bool buttonClose; // Schliessen ueber OK/Abbrechen (nicht ueber X)
        System.Windows.Forms.Timer previewTimer;

        public SettingsForm(AppCtx c)
        {
            ctx = c;
            Text = L.T("Akku-Anzeige – Einstellungen");
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(672, 676);
            Icon = ctx.WindowIcon;

            tabs = new SimpleTabs { Bounds = new Rectangle(12, 12, 648, 612) };

            // --- je Monitor ein Reiter (aktive zuerst, dann deaktivierte / nicht verbundene) ---
            var mons = Displays.GetAll();
            if (ctx.RememberMonitors(mons)) { try { ctx.Settings.Save(); } catch { } }
            int num = 1;
            var listed = new HashSet<string>();
            foreach (var mi in mons)
            {
                var k = ctx.Settings.KnownMonitors.First(x => x.Id == mi.Id);
                listed.Add(mi.Id);
                pages.Add(new MonitorPage(this, num++, k.Clone(), mi, ctx));
            }
            foreach (var k in ctx.Settings.KnownMonitors)
                if (!listed.Contains(k.Id)) pages.Add(new MonitorPage(this, num++, k.Clone(), null, ctx));
            foreach (var p in pages) tabs.AddPage(p);

            // --- Grenzwerte ---
            var gen = new Panel { Text = L.T("Grenzwerte") };
            var gAlarm = new ThemedGroupBox { Text = L.T("Alarm (gilt für alle Monitore)"), Bounds = new Rectangle(8, 10, 624, 100) };
            trk = new TrackBar { Minimum = 5, Maximum = 50, TickFrequency = 5, SmallChange = 1, LargeChange = 5, Bounds = new Rectangle(12, 24, 440, 45), Value = Math.Max(5, Math.Min(50, ctx.Settings.WarnLevel)) };
            lblTrk = new Label { Bounds = new Rectangle(18, 70, 430, 20) };
            trk.ValueChanged += delegate { UpdateTrkLabel(); LimitsChanged(); };
            UpdateTrkLabel();
            var btnTest = new Button { Text = L.T("Test-Alarm"), Bounds = new Rectangle(466, 26, 146, 32) };
            btnTest.Click += delegate
            {
                ctx.ShowAlarm(L.F("Akku fast leer: {0}", "G502 X Plus"), L.F("TEST – {0} hat nur noch {1} % Akku – bitte aufladen!", "G502 X Plus", trk.Value),
                              soundFile ?? "", trkVol != null ? (int?)trkVol.Value : null);
            };
            gAlarm.Controls.AddRange(new Control[] { trk, lblTrk, btnTest });

            // Farbbereiche: eine Linie mit Rot | Orange | Gruen und zwei Griffen
            var gCol = new ThemedGroupBox { Text = L.T("Farbbereiche (gilt für alle Monitore)"), Bounds = new Rectangle(8, 120, 624, 164) };
            colBar = new ColorRangeBar { Bounds = new Rectangle(12, 24, 600, 50) };
            colBar.OrangeFrom = ctx.Settings.OrangeFrom;
            colBar.GreenFrom = ctx.Settings.GreenFrom;
            lblRed = new Label { Bounds = new Rectangle(16, 78, 190, 20), ForeColor = Color.FromArgb(225, 50, 50), Tag = "keep" };
            lblOrange = new Label { Bounds = new Rectangle(214, 78, 200, 20), ForeColor = Color.FromArgb(235, 125, 0), Tag = "keep" };
            lblGreen = new Label { Bounds = new Rectangle(422, 78, 190, 20), ForeColor = Color.FromArgb(50, 170, 80), Tag = "keep" };
            var lblColHint = new Label { Text = L.T("Die beiden Griffe auf der Linie verschieben: links Rot, Mitte Orange, rechts Grün."), Bounds = new Rectangle(16, 102, 596, 18), ForeColor = Color.DimGray };
            gCol.Controls.AddRange(new Control[] { colBar, lblRed, lblOrange, lblGreen, lblColHint });
            colBar.ValueChanged += delegate { UpdateColLabels(); LimitsChanged(); };

            // eigene Farben: Klick auf ein Feld oeffnet die Windows-Farbauswahl
            colors = ctx.SavedColors;
            gCol.Controls.Add(new Label { Text = L.T("Farben:"), Bounds = new Rectangle(16, 130, 56, 20) });
            string[] colNames = { L.T("Rot"), L.T("Orange"), L.T("Grün"), L.T("Laden") };
            colButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var b = new Button { Text = "   " + colNames[i], Bounds = new Rectangle(72 + i * 106, 125, 100, 28), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleRight };
                b.Click += delegate { PickColor(slot, colNames[slot]); };
                colButtons[i] = b;
                gCol.Controls.Add(b);
            }
            var btnColReset = new Button { Text = L.T("Standardfarben"), Bounds = new Rectangle(498, 125, 114, 28) };
            btnColReset.Click += delegate { colors = new int[4]; ColorsChanged(); };
            gCol.Controls.Add(btnColReset);
            ColorsChanged();
            UpdateColLabels();

            chkGray = new CheckBox
            {
                Text = L.T("Getrennte Geräte und Geräte im Ruhemodus grau anzeigen (letzter Akkustand bleibt sichtbar)"),
                Bounds = new Rectangle(16, 294, 610, 24),
                Checked = ctx.Settings.GrayOffline
            };
            chkGray.CheckedChanged += delegate { LimitsChanged(); };

            // Maus/Tastatur auch grau, wenn sie eine Weile nicht benutzt wurden (Beleuchtung aus)
            chkIdle = new CheckBox { Text = L.T("Maus/Tastatur auch grau anzeigen, wenn sie unbenutzt sind seit"), Bounds = new Rectangle(16, 324, 420, 24), Checked = ctx.Settings.IdleGray };
            numIdle = new NumericUpDown { Minimum = 1, Maximum = 60, Value = Math.Max(1, Math.Min(60, ctx.Settings.IdleMinutes)), Bounds = new Rectangle(436, 324, 56, 24) };
            var lblIdleMin = new Label { Text = L.T("Minuten"), Bounds = new Rectangle(498, 328, 80, 20) };
            numIdle.Enabled = chkIdle.Checked;
            chkIdle.CheckedChanged += delegate { numIdle.Enabled = chkIdle.Checked; IdleChanged(); };
            numIdle.ValueChanged += delegate { IdleChanged(); };

            var lblInfo = new Label
            {
                Text = L.K("GRENZ_INFO",
                       "Alarm kommt für alle Geräte, die auf mindestens einem Monitor angezeigt werden – als eigenes Warnfenster " +
                       "mit Warnton, das auch bei \"Nicht stören\", stummgeschalteten Benachrichtigungen und im Spielmodus erscheint.\n\n" +
                       "Logitech-Geräte kommen aus G HUB – ohne G HUB aus LGSTray oder direkt vom Empfänger (HID++). " +
                       "Bluetooth-Geräte erscheinen, sobald Windows ihren Akkustand kennt; AirPods/Beats über ihre Bluetooth-LE-Signale.\n\n" +
                       "Änderungen siehst du sofort als Vorschau. " +
                       "Gespeichert wird erst mit \"Übernehmen\" oder \"OK\" – \"Abbrechen\" verwirft sie."),
                Bounds = new Rectangle(16, 364, 600, 160),
                ForeColor = Color.DimGray
            };
            gen.Controls.AddRange(new Control[] { gAlarm, gCol, chkGray, chkIdle, numIdle, lblIdleMin, lblInfo });
            tabs.AddPage(gen);

            // --- Einstellungen (Programm + Darstellung) ---
            var opt = new ScrollPage { Text = L.T("Einstellungen"), AutoScroll = true }; // scrollbar – Platz fuer weitere Bereiche
            var gProg = new ThemedGroupBox { Text = L.T("Programm"), Bounds = new Rectangle(8, 10, 608, 128) };
            chkAuto = new CheckBox { Text = L.T("Mit Windows starten"), Bounds = new Rectangle(12, 24, 400, 24), Checked = AppCtx.AutostartEnabled };
            chkMin = new CheckBox { Text = L.T("Minimiert starten (nur die Kacheln, ohne dieses Fenster)"), Bounds = new Rectangle(12, 52, 500, 24), Checked = ctx.Settings.StartMinimized };
            var lblClose = new Label { Text = L.T("Beim Schließen des Fensters (X):"), Bounds = new Rectangle(14, 90, 200, 20) };
            cmbClose = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(216, 86, 290, 24) };
            cmbClose.Items.AddRange(new object[] { L.T("Minimieren (läuft im Hintergrund weiter)"), L.T("Programm komplett beenden") });
            cmbClose.SelectedIndex = ctx.Settings.CloseExits ? 1 : 0;
            gProg.Controls.AddRange(new Control[] { chkAuto, chkMin, lblClose, cmbClose });

            var gTheme = new ThemedGroupBox { Text = L.T("Darstellung"), Bounds = new Rectangle(8, 148, 608, 134) };
            var lblLang = new Label { Text = L.T("Sprache:"), Bounds = new Rectangle(14, 100, 200, 20) };
            cmbLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(224, 96, 140, 24) };
            foreach (var kv in L.Languages) cmbLang.Items.Add(kv.Value);
            cmbLang.SelectedIndex = Math.Max(0, Array.FindIndex(L.Languages, kv => kv.Key == ctx.Settings.Language));
            var lblLangHint = new Label { Text = L.T("Die Sprache wird nach \"Übernehmen\" sofort umgestellt."), Bounds = new Rectangle(372, 100, 228, 30), ForeColor = Color.DimGray };
            chkThemeAuto = new CheckBox { Text = L.T("Fensterdesign automatisch an Windows anpassen (heller / dunkler Modus)"), Bounds = new Rectangle(12, 24, 600, 24), Checked = ctx.Settings.ThemeAuto };
            lblThemeMan = new Label { Text = L.T("Design, wenn nicht automatisch:"), Bounds = new Rectangle(32, 62, 190, 20) };
            cmbTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(224, 58, 140, 24) };
            cmbTheme.Items.AddRange(new object[] { L.T("Hell"), L.T("Dunkel") });
            cmbTheme.SelectedIndex = ctx.Settings.ThemeDark ? 1 : 0;
            gTheme.Controls.AddRange(new Control[] { chkThemeAuto, lblThemeMan, cmbTheme, lblLang, cmbLang, lblLangHint });
            chkThemeAuto.CheckedChanged += delegate { ApplyTheme(); };
            cmbTheme.SelectedIndexChanged += delegate { ApplyTheme(); };

            // Alarm-Ton: Lautstaerke + eigene Sounddatei
            var gSound = new ThemedGroupBox { Text = L.T("Alarm-Ton"), Bounds = new Rectangle(8, 292, 608, 152) };
            lblVol = new Label { Bounds = new Rectangle(14, 24, 400, 20) };
            trkVol = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Bounds = new Rectangle(12, 44, 440, 45), Value = Math.Max(0, Math.Min(100, ctx.Settings.AlarmVolume)) };
            trkVol.ValueChanged += delegate { UpdateVolLabel(); };
            UpdateVolLabel();
            var btnPlay = new Button { Text = L.T("▶  Ton testen"), Bounds = new Rectangle(450, 44, 146, 32) };
            btnPlay.Click += delegate { AlarmSound.Play(soundFile, trkVol.Value); };
            var lblFile = new Label { Text = L.T("Sound:"), Bounds = new Rectangle(14, 98, 80, 20) };
            cmbSound = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(96, 95, 294, 24), MaxDropDownItems = 20 };
            FillSounds(ctx.Settings.AlarmFile);
            cmbSound.SelectedIndexChanged += delegate
            {
                var si = cmbSound.SelectedItem as SoundItem;
                soundFile = si != null ? si.Path : null;
            };
            var btnBrowse = new Button { Text = L.T("Durchsuchen …"), Bounds = new Rectangle(396, 93, 100, 28) };
            var btnStd = new Button { Text = L.T("Standard"), Bounds = new Rectangle(502, 93, 94, 28) };
            btnBrowse.Click += delegate
            {
                using (var dlg = new OpenFileDialog { Title = L.T("Alarm-Sounddatei wählen"), Filter = L.T("Audiodateien") + " (*.wav;*.mp3;*.wma;*.m4a;*.aac)|*.wav;*.mp3;*.wma;*.m4a;*.aac|" + L.T("Alle Dateien") + " (*.*)|*.*" })
                {
                    if (!string.IsNullOrEmpty(soundFile) && File.Exists(soundFile)) dlg.InitialDirectory = Path.GetDirectoryName(soundFile);
                    if (dlg.ShowDialog(this) == DialogResult.OK) FillSounds(dlg.FileName);
                }
            };
            btnStd.Click += delegate { cmbSound.SelectedIndex = 0; };
            var lblSoundHint = new Label { Text = L.T("Windows-Sound im Menü oder eigene Datei (WAV, MP3, WMA, M4A, AAC)."), Bounds = new Rectangle(14, 126, 600, 18), ForeColor = Color.DimGray };
            gSound.Controls.AddRange(new Control[] { lblVol, trkVol, btnPlay, lblFile, cmbSound, btnBrowse, btnStd, lblSoundHint });

            new ToolTip { AutoPopDelay = 20000 }.SetToolTip(chkMin, L.K("OPT_INFO",
                       "Minimiert: Akku-Kacheln und Alarm laufen im Hintergrund weiter. Das Fenster öffnest du wieder über " +
                       "die Desktop-Verknüpfung \"Akku-Anzeige\" oder per Doppelklick auf das Symbol im Infobereich.\n\n" +
                       "Die Akku-Kacheln auf der Taskleiste passen sich immer automatisch an die Farbe der Taskleiste an."));

            // Datenquellen: welche (kompatiblen, installierten) Programme mit der Akku-Anzeige verknuepft sind.
            // "Treiber & Geräte suchen" erkennt nachtraeglich installierte Software und angeschlossene Hardware.
            gSrc = new ThemedGroupBox { Text = L.T("Datenquellen (verknüpfte Treiber-Software)"), Bounds = new Rectangle(8, 454, 608, 118) };
            lblSrcHint = new Label { Text = L.T("Nicht kompatible oder nicht installierte Programme erscheinen hier nicht."), Bounds = new Rectangle(14, 96, 430, 18), ForeColor = Color.DimGray, AutoEllipsis = true };
            btnScan = new Button { Text = L.T("Treiber && Geräte suchen"), Bounds = new Rectangle(450, 90, 146, 26) };
            btnScan.Click += delegate { ScanSources(); };
            gSrc.Controls.Add(lblSrcHint);
            gSrc.Controls.Add(btnScan);
            BuildSources(null);
            // Info: Version, Hersteller, Update-Suche (frueher eigenes Fenster)
            gInfo = new ThemedGroupBox { Text = L.T("Info"), Bounds = new Rectangle(8, 576, 608, 176) };
            var pic = new PictureBox { Bounds = new Rectangle(14, 26, 56, 56), SizeMode = PictureBoxSizeMode.Zoom };
            var icoBytes = IconTools.Resource("AkkuAnzeige.win.ico");
            if (icoBytes != null) pic.Image = IconTools.FromIco(icoBytes, 56);
            var lblApp = new Label { Text = "Akku-Anzeige", Font = new Font("Segoe UI Semibold", 13f), Bounds = new Rectangle(82, 22, 500, 26) };
            var lblVer = new Label { Text = L.F("Version {0}", AppInfo.VersionText) + "   ·   " + L.F("Hersteller: {0}", AppInfo.Publisher), Bounds = new Rectangle(84, 50, 510, 18) };
            var lblPow = new Label { Text = AppInfo.Copyright + "   ·   " + AppInfo.PoweredBy, Bounds = new Rectangle(84, 70, 510, 18), ForeColor = Color.DimGray };
            var btnUpd = new Button { Text = L.T("Nach Updates suchen"), Bounds = new Rectangle(14, 96, 170, 30) };
            var btnWeb = new Button { Text = L.T("Website öffnen"), Bounds = new Rectangle(190, 96, 150, 30) };
            var lblUpd = new Label { Bounds = new Rectangle(14, 132, 584, 36) };
            var btnLog = new Button { Text = L.T("Changelog"), Bounds = new Rectangle(346, 96, 130, 30) };
            btnUpd.Click += delegate { UpdateUi.Check(this, btnUpd, lblUpd, ctx.WindowIcon, DarkWanted(), ctx.BeginQuit); };
            btnWeb.Click += delegate { UpdateUi.OpenWebsite(lblUpd); };
            btnLog.Click += delegate { using (var f = new ChangelogForm(ctx.WindowIcon, DarkWanted())) f.ShowDialog(this); };
            var btnBug = new Button { Text = "🐞 " + L.T("Bug melden"), Bounds = new Rectangle(482, 96, 112, 30) };
            btnBug.Click += delegate { using (var f = new BugReportForm(ctx.WindowIcon, DarkWanted())) f.ShowDialog(this); };
            gInfo.Controls.AddRange(new Control[] { pic, lblApp, lblVer, lblPow, btnUpd, btnWeb, btnLog, btnBug, lblUpd });
            var spacer = new Label { Bounds = new Rectangle(8, 752, 10, 12) }; // etwas Abstand unten beim Scrollen

            opt.Controls.AddRange(new Control[] { gProg, gTheme, gSound, gSrc, gInfo, spacer });
            optPage = opt;
            tabs.AddPage(opt);

            // --- unten ---
            var btnIdent = new Button { Text = L.T("Monitore identifizieren"), Bounds = new Rectangle(12, 634, 170, 30) };
            var btnOk = new Button { Text = "OK", Bounds = new Rectangle(384, 634, 88, 30) };
            var btnApply = new Button { Text = L.T("Übernehmen"), Bounds = new Rectangle(478, 634, 88, 30) };
            var btnClose = new Button { Text = L.T("Abbrechen"), Bounds = new Rectangle(572, 634, 88, 30) };
            btnIdent.Click += delegate { Identify(); };
            btnOk.Click += delegate { if (!ApplyAll(false)) return; buttonClose = true; Close(); };
            btnApply.Click += delegate { ApplyAll(true); };
            btnClose.Click += delegate { buttonClose = true; Close(); };
            CancelButton = btnClose;
            Controls.AddRange(new Control[] { tabs, btnIdent, btnOk, btnApply, btnClose });

            previewTimer = new System.Windows.Forms.Timer { Interval = 120 };
            previewTimer.Tick += delegate
            {
                previewTimer.Stop();
                ctx.SetPreview(pages.Select(p => p.Entry.Clone()).ToList());
            };
            FormClosed += (s, e) =>
            {
                previewTimer.Stop();
                ctx.EndPreview(); // nicht Uebernommenes verwerfen
                // X gedrueckt und "komplett beenden" gespeichert -> Programm beenden
                if (!buttonClose && e.CloseReason == CloseReason.UserClosing && ctx.Settings.CloseExits)
                    ctx.BeginQuit();
            };

            // Reiter des Monitors oeffnen, auf dem gerade die Maus ist
            var cur = Screen.FromPoint(Cursor.Position).DeviceName;
            var start = pages.FirstOrDefault(p => p.GdiName == cur);
            if (start != null) tabs.SelectPage(start);

            // Strg+Tab / Strg+Umschalt+Tab wechselt die Reiter
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Tab) { tabs.Next(e.Shift ? -1 : 1); e.Handled = true; }
            };
            HandleCreated += delegate { ApplyTheme(); };
            Shown += delegate { ready = true; };
        }

        bool DarkWanted()
        {
            return chkThemeAuto.Checked ? Theme.SystemAppsDark() : cmbTheme.SelectedIndex == 1;
        }

        // Design sofort anwenden (gespeichert wird mit Übernehmen/OK)
        public void ApplyTheme()
        {
            cmbTheme.Enabled = !chkThemeAuto.Checked;
            // Beschriftung nicht ausgrauen (das zeichnet Windows dunkel), sondern nur gedaempft einfaerben
            lblThemeMan.Tag = chkThemeAuto.Checked ? "dim" : "fg";
            if (!IsHandleCreated) return;
            bool dark = DarkWanted();
            SuspendLayout();
            Theme.Apply(this, dark);
            ResumeLayout();
            Invalidate(true);
        }

        // Windows hat zwischen hell/dunkel gewechselt
        public void SystemThemeChanged() { if (chkThemeAuto.Checked) ApplyTheme(); }

        bool ready; // erst nach dem Anzeigen auf Aenderungen reagieren

        public void SelectTab(int i) { tabs.SelectIndex(i); }

        GroupBox gInfo;
        Panel optPage;

        // Reiter "Einstellungen" oeffnen und zum Info-Bereich scrollen
        public void ShowInfo()
        {
            if (optPage == null) return;
            tabs.SelectPage(optPage);
            optPage.ScrollControlIntoView(gInfo);
        }

        public void Changed()
        {
            if (!ready || previewTimer == null) return;
            previewTimer.Stop();
            previewTimer.Start();
        }

        void UpdateTrkLabel() { lblTrk.Text = L.F("Alarm, wenn ein Gerät {0} % oder weniger hat", trk.Value); }

        void UpdateVolLabel() { lblVol.Text = L.F("Lautstärke des Alarms: {0} %", trkVol.Value); }

        class SoundItem
        {
            public string Name, Path;
            public override string ToString() { return Name; }
        }

        // Auswahl: Standard-Warnton (Vorgabe) | eigene Datei | Windows-Sounds aus C:\Windows\Media
        void FillSounds(string selectPath)
        {
            cmbSound.BeginUpdate();
            cmbSound.Items.Clear();
            cmbSound.Items.Add(new SoundItem { Name = L.T("Standard-Warnton (Akku-Anzeige)"), Path = null });
            var winFiles = new List<string>();
            try
            {
                string media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
                winFiles = Directory.GetFiles(media, "*.wav").OrderBy(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToList();
            }
            catch { }
            bool isWin = !string.IsNullOrEmpty(selectPath) && winFiles.Any(f => string.Equals(f, selectPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(selectPath) && !isWin)
                cmbSound.Items.Add(new SoundItem
                {
                    Name = L.F("Eigene Datei: {0}", System.IO.Path.GetFileName(selectPath)) + (File.Exists(selectPath) ? "" : L.T(" (nicht gefunden)")),
                    Path = selectPath
                });
            foreach (var f in winFiles)
                cmbSound.Items.Add(new SoundItem { Name = "Windows: " + System.IO.Path.GetFileNameWithoutExtension(f), Path = f });
            cmbSound.EndUpdate();

            int idx = 0;
            if (!string.IsNullOrEmpty(selectPath))
                for (int i = 0; i < cmbSound.Items.Count; i++)
                    if (string.Equals(((SoundItem)cmbSound.Items[i]).Path, selectPath, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            cmbSound.SelectedIndex = idx;
            soundFile = ((SoundItem)cmbSound.Items[idx]).Path;
        }

        GroupBox gSrc;
        Label lblSrcHint;
        Button btnScan;
        readonly ToolTip srcTip = new ToolTip { AutoPopDelay = 20000 };
        readonly Dictionary<string, bool> srcState = new Dictionary<string, bool>(); // Haken, die schon im Fenster standen

        // Liste der Datenquellen aufbauen; scan != null: Ergebnis von "Treiber & Geräte suchen"
        void BuildSources(SourceScan scan)
        {
            foreach (var cb in srcChecks) { srcState[(string)cb.Tag] = cb.Checked; gSrc.Controls.Remove(cb); cb.Dispose(); }
            srcChecks.Clear();
            bool en = L.Current == "en";
            var srcs = Links.BuiltIns.Select(b => new { Key = b.Key, Name = en ? b.NameEn : b.NameDe, Note = en ? b.NoteEn : b.NoteDe }).ToList();
            var vendors = scan != null ? scan.Vendors : Vendors.Detect(true);
            foreach (var v in vendors.Where(x => x.Support == VendorSupport.Supported))
                srcs.Add(new { Key = v.Key, Name = v.Name, Note = en ? v.NoteEn : v.NoteDe });
            int sx = 12, sy = 22;
            var newOnes = new List<string>();
            foreach (var src in srcs)
            {
                bool known = srcState.ContainsKey(src.Key);
                bool isNew = scan != null && !known;  // erst durch den Scan aufgetaucht -> nur anbieten, nicht anhaken
                bool found = false;
                if (scan != null) scan.Present.TryGetValue(src.Key, out found);
                string text = src.Name + (isNew ? " (" + L.T("neu") + ")" : "") + (found ? " ✓" : "");
                var cb = new CheckBox { Text = text, Tag = src.Key, Bounds = new Rectangle(sx, sy, 190, 24), AutoEllipsis = true,
                                        Checked = known ? srcState[src.Key] : (isNew ? false : Links.Enabled(src.Key)) };
                srcTip.SetToolTip(cb, src.Note + (scan == null ? "" : "\n" + (found ? L.T("Auf diesem PC gefunden.") : L.T("Auf diesem PC nicht gefunden."))));
                if (isNew) newOnes.Add(src.Name);
                srcChecks.Add(cb);
                gSrc.Controls.Add(cb);
                if (sx < 404) sx += 196; else { sx = 12; sy += 24; }
            }
            if (scan == null) return;
            // Ergebnis zusammenfassen
            var foundNames = srcs.Where(x => x.Key != Links.Bluetooth && scan.Present.ContainsKey(x.Key) && scan.Present[x.Key]).Select(x => x.Name).ToList();
            var parts = new List<string>();
            parts.Add(foundNames.Count > 0 ? L.F("Gefunden: {0}", string.Join(", ", foundNames)) : L.T("Keine weiteren kompatiblen Geräte oder Programme gefunden."));
            if (newOnes.Count > 0) parts.Add(L.T("Neue zum Verknüpfen anhaken und \"Übernehmen\"."));
            var incompatible = scan.Vendors.Where(v => v.Support != VendorSupport.Supported).Select(v => v.Name).ToList();
            if (incompatible.Count > 0) parts.Add(L.F("Erkannt, aber nicht kompatibel: {0}", string.Join(", ", incompatible)));
            lblSrcHint.Text = string.Join(" · ", parts);
            srcTip.SetToolTip(lblSrcHint, string.Join("\n", parts));
            ApplyTheme(); // neue Haekchen im aktuellen Design
        }

        void ScanSources()
        {
            btnScan.Enabled = false;
            lblSrcHint.Text = L.T("Suche nach Treibern und Geräten …");
            ThreadPool.QueueUserWorkItem(delegate
            {
                SourceScan r = null;
                try { r = SourceScan.Run(); } catch (Exception ex) { Program.LogError(ex); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        btnScan.Enabled = true;
                        if (r != null) BuildSources(r); else lblSrcHint.Text = L.T("Suche fehlgeschlagen.");
                    }));
                }
                catch { }
            });
        }

        int[] colors;          // eingestellte Farben (ARGB, 0 = Standard), Reihenfolge wie Palette
        Button[] colButtons;

        Color ColorAt(int slot) { return colors[slot] == 0 ? Palette.Default[slot] : Color.FromArgb(255, Color.FromArgb(colors[slot])); }

        void PickColor(int slot, string name)
        {
            using (var dlg = new ColorPicker { Color = ColorAt(slot), FullOpen = true, Title = L.F("Farbe für \"{0}\" wählen", name), ResetText = L.T("Standardfarbe") })
            {
                var r = dlg.ShowDialog(this);
                if (dlg.ResetRequested) colors[slot] = 0;
                else if (r == DialogResult.OK) colors[slot] = dlg.Color.ToArgb() == Palette.Default[slot].ToArgb() ? 0 : dlg.Color.ToArgb();
                else return;
            }
            ColorsChanged();
        }

        // Farbfelder, Farblinie und Beschriftungen auffrischen + Vorschau auf den Kacheln
        void ColorsChanged()
        {
            for (int i = 0; i < 4; i++)
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var br = new SolidBrush(ColorAt(i))) g.FillRectangle(br, 0, 0, 16, 16);
                    g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
                }
                var old = colButtons[i].Image;
                colButtons[i].Image = bmp;
                if (old != null) old.Dispose();
            }
            colBar.SetColors(ColorAt(Palette.Red), ColorAt(Palette.Orange), ColorAt(Palette.Green));
            lblRed.ForeColor = ColorAt(Palette.Red);
            lblOrange.ForeColor = ColorAt(Palette.Orange);
            lblGreen.ForeColor = ColorAt(Palette.Green);
            if (ready) ctx.SetColorPreview((int[])colors.Clone());
        }

        void UpdateColLabels()
        {
            lblRed.Text = L.F("Rot: 0 – {0} %", colBar.OrangeFrom - 1);
            lblOrange.Text = L.F("Orange: {0} – {1} %", colBar.OrangeFrom, colBar.GreenFrom - 1);
            lblGreen.Text = L.F("Grün: {0} – 100 %", colBar.GreenFrom);
        }

        // Grenzwerte sofort als Vorschau auf den Kacheln zeigen
        void LimitsChanged()
        {
            if (!ready || colBar == null || chkGray == null) return;
            ctx.SetLimitPreview(trk.Value, colBar.OrangeFrom, colBar.GreenFrom, chkGray.Checked);
        }

        void IdleChanged()
        {
            if (!ready || chkIdle == null || numIdle == null) return;
            ctx.SetIdlePreview(chkIdle.Checked, (int)numIdle.Value);
        }

        public void UpdateDevices(List<DeviceInfo> list) { foreach (var p in pages) p.UpdateDevices(list); }

        // true = Fenster bleibt gueltig; false = Fenster wurde fuer die neue Sprache neu geoeffnet
        bool ApplyAll(bool reopenOnLanguageChange)
        {
            previewTimer.Stop();
            try { AppCtx.SetAutostart(chkAuto.Checked); } catch { }
            ctx.Settings.IdleGray = chkIdle.Checked;
            ctx.Settings.IdleMinutes = (int)numIdle.Value;
            ctx.Settings.AlarmVolume = trkVol.Value;
            ctx.Settings.AlarmFile = soundFile;
            ctx.Settings.ThemeAuto = chkThemeAuto.Checked;
            ctx.Settings.ThemeDark = cmbTheme.SelectedIndex == 1;
            foreach (var cb in srcChecks) Links.Set((string)cb.Tag, cb.Checked);
            ctx.Poll();
            string newLang = L.Languages[Math.Max(0, cmbLang.SelectedIndex)].Key;
            bool langChanged = newLang != ctx.Settings.Language;
            ctx.Settings.Language = newLang;
            ctx.ApplySettings(pages.Select(p => p.Entry.Clone()).ToList(), trk.Value, chkMin.Checked, cmbClose.SelectedIndex == 1,
                              colBar.OrangeFrom, colBar.GreenFrom, chkGray.Checked, (int[])colors.Clone());
            if (langChanged)
            {
                // Sprache umstellen: Fenster und Menue in der neuen Sprache neu aufbauen
                L.Current = newLang;
                ctx.RebuildMenu();
                if (!reopenOnLanguageChange) return true;
                int tab = tabs.SelectedIndex;
                buttonClose = true;
                Close();
                ctx.ShowSettings(tab);
                return false;
            }
            return true;
        }

        void Identify()
        {
            foreach (var s in Screen.AllScreens)
            {
                var page = pages.FirstOrDefault(p => p.GdiName == s.DeviceName);
                if (page == null) continue;
                var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    TopMost = true,
                    ShowInTaskbar = false,
                    BackColor = Color.FromArgb(32, 32, 32),
                    Size = new Size(260, 240)
                };
                f.Location = new Point(s.Bounds.Left + (s.Bounds.Width - 260) / 2, s.Bounds.Top + (s.Bounds.Height - 240) / 2);
                var lblN = new Label { Text = page.Entry.Name, Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Silver, Font = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel) };
                f.Controls.Add(new Label { Text = page.Num.ToString(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 110f, FontStyle.Bold, GraphicsUnit.Pixel) });
                f.Controls.Add(lblN);
                lblN.SendToBack();
                f.Show();
                var t = new System.Windows.Forms.Timer { Interval = 2000 };
                t.Tick += delegate { t.Stop(); t.Dispose(); f.Close(); };
                t.Start();
            }
        }
    }

    // ======================= Start =======================
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var mutex = new Mutex(true, @"Local\AkkuAnzeige_Mutex", out created);
            var evt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AkkuAnzeige_ShowSettings");
            if (!created)
            {
                evt.Set(); // laeuft schon -> Einstellungsfenster der laufenden Instanz oeffnen
                return;
            }
            Native.SetProcessDPIAware();
            // Fehler protokollieren statt Fehlerdialog
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogError(e.Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool showSettings = !args.Any(a => a.Equals("/autostart", StringComparison.OrdinalIgnoreCase));
            // auch Fehler beim Start / in Hintergrund-Threads protokollieren
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
            try { Application.Run(new AppCtx(evt, showSettings)); }
            catch (Exception ex) { LogError(ex); throw; }
            GC.KeepAlive(mutex);
        }

        public static void Log(string msg) { LogError(null, msg); }
        public static void LogError(Exception ex) { LogError(ex, null); }

        static void LogError(Exception ex, string msg)
        {
            if (ex == null && msg == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "  " + (msg ?? ex.ToString()) + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        static void LogErrorOld(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "  " + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
