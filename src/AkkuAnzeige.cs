// Akku-Anzeige: Akku-Kacheln auf der Taskleiste fuer Logitech- (via LGSTray) und Bluetooth-Geraete.
// Kompiliert mit dem .NET Framework csc (C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// Version / Hersteller: siehe AssemblyInfo.cs (gemeinsam mit dem Setup)

namespace AkkuAnzeige
{
    public class DeviceInfo
    {
        public string Id;
        public string Name;
        public string Source;
        public int? Percent;
        public bool Charging;
        public bool Connected;
        public string DefaultIcon;
        public string InputKey; // Empfaenger "VID:PID" fuer die Erkennung "unbenutzt" (nur Logitech)
    }

    static class Icons
    {
        public static readonly string[] Names = { "Maus", "Tastatur", "Kopfhörer", "Lautsprecher", "Controller", "Handy", "Bluetooth" };
        public static string Glyph(string n)
        {
            switch (n)
            {
                case "Maus": return "";
                case "Tastatur": return "";
                case "Kopfhörer": return "";
                case "Lautsprecher": return "";
                case "Controller": return "";
                case "Handy": return "";
                default: return "";
            }
        }
    }

    // ======================= Win32 =======================
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }

        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        static readonly Dictionary<uint, string> procNames = new Dictionary<uint, string>();
        public static string ProcessNameOf(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string n;
            if (procNames.TryGetValue(pid, out n)) return n;
            try { n = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { n = ""; }
            if (procNames.Count > 200) procNames.Clear();
            procNames[pid] = n;
            return n;
        }

        // Windows-eigene Oberflaechen (Startmenue, Suche, Infobereich, Schnelleinstellungen, Bluetooth, Widgets ...)
        static readonly HashSet<string> shellProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "StartMenuExperienceHost", "SearchHost", "SearchApp", "SearchUI", "ShellExperienceHost",
            "ShellHost", "TextInputHost", "LockApp", "Widgets", "WidgetBoard", "Cortana", "PeopleExperienceHost",
            "ApplicationFrameHost", "SystemSettings", "Taskmgr", "ScreenClippingHost", "SnippingTool"
        };
        static readonly HashSet<string> shellClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "ControlCenterWindow",
            "NotifyIconOverflowWindow", "Shell_InputSwitchTopLevelWindow", "ForegroundStaging", "MultitaskingViewFrame",
            "TaskListThumbnailWnd", "XamlExplorerHostIslandWindow_WASDK"
        };
        public static bool IsShellWindow(IntPtr h)
        {
            return shellClasses.Contains(ClassOf(h)) || shellProcs.Contains(ProcessNameOf(h));
        }

        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);

        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

        // Taskleiste des Monitors finden (Haupt- oder Zweit-Taskleiste)
        public static IntPtr FindTaskbar(string screenName)
        {
            foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr h = IntPtr.Zero;
                while ((h = FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                {
                    RECT r;
                    if (!GetWindowRect(h, out r)) continue;
                    var s = System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
                    if (s.DeviceName == screenName) return h;
                }
            }
            return IntPtr.Zero;
        }
    }

    // ======================= Taskleisten-Bewegung verfolgen =======================
    // "Taskleiste automatisch ausblenden": Windows verschiebt das Taskleistenfenster beim Ein-/Ausfahren
    // schrittweise (~15-30 ms je Schritt) und meldet jeden Schritt als EVENT_OBJECT_LOCATIONCHANGE.
    // Wir haengen uns an genau diese Meldung (nur fuer den Explorer-Prozess) – kein Abfrage-Timer,
    // im Ruhezustand also keinerlei CPU-Last, und die Kacheln bewegen sich im selben Takt wie die Taskleiste.
    class TaskbarHook
    {
        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;
        readonly Native.WinEventProc cb; // Referenz halten, sonst raeumt der GC den Callback weg
        readonly Action<IntPtr> moved;
        IntPtr hook = IntPtr.Zero;
        uint pid;

        public TaskbarHook(Action<IntPtr> onTaskbarMoved)
        {
            moved = onTaskbarMoved;
            cb = (h, ev, hwnd, idObject, idChild, thread, time) =>
            {
                if (idObject != 0 || idChild != 0) return; // OBJID_WINDOW: nur das Fenster selbst, nicht Cursor/Caret
                try { moved(hwnd); } catch (Exception ex) { Program.LogError(ex); }
            };
        }

        // an = Hook auf den aktuellen Explorer setzen (auch nach einem Explorer-Neustart), aus = entfernen
        public void Watch(bool on)
        {
            uint cur = 0;
            if (on)
            {
                IntPtr tb = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
                if (tb != IntPtr.Zero) Native.GetWindowThreadProcessId(tb, out cur);
            }
            if (cur == pid && (hook != IntPtr.Zero) == (cur != 0)) return;
            if (hook != IntPtr.Zero) { Native.UnhookWinEvent(hook); hook = IntPtr.Zero; }
            pid = cur;
            if (cur != 0) hook = Native.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, cb, cur, 0, WINEVENT_OUTOFCONTEXT);
        }
    }

    // ======================= Datenquellen =======================
    static class DeviceReader
    {
        const string LgsUrl = "http://localhost:12321";

        public static List<DeviceInfo> ReadAll()
        {
            var list = new List<DeviceInfo>();
            // Logitech: direkt aus G HUB (Akku + Aktiv-Status); LGSTray nur, wenn G HUB nicht erreichbar ist
            // nur verknuepfte Quellen (Einstellungen -> Datenquellen / Setup)
            List<DeviceInfo> logi = null;
            if (Links.Enabled("ghub")) { try { logi = GHub.Read(); } catch { logi = null; } }
            if (logi == null && Links.Enabled("lgstray")) { try { logi = ReadLogitech(); } catch { logi = null; } }
            if (logi != null) list.AddRange(logi);
            if (Links.Enabled(Links.Bluetooth)) { try { list.AddRange(ReadBluetooth()); } catch { } }
            return list;
        }

        // ---------- Logitech ueber LGSTray ----------
        static string Http(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 3000; req.ReadWriteTimeout = 3000; req.Proxy = null;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch { return null; }
        }

        static string Tag(string xml, string tag)
        {
            var m = Regex.Match(xml, "<" + tag + ">(.*?)</" + tag + ">", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : "";
        }

        static string CleanLogiName(string n)
        {
            foreach (var cut in new[] { " Wireless", " LIGHTSPEED", " Gaming", " Mechanical" })
            {
                int i = n.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (i > 0) n = n.Substring(0, i);
            }
            return n.Trim();
        }

        static List<DeviceInfo> ReadLogitech()
        {
            var result = new Dictionary<string, DeviceInfo>();
            var ghub = new Dictionary<string, bool>();
            string index = Http(LgsUrl + "/devices");
            if (index == null) return new List<DeviceInfo>();
            var seen = new HashSet<string>();
            var acc = new Dictionary<string, LogiAcc>();
            foreach (Match m in Regex.Matches(index, "href=\"/device/([^\"]+)\""))
            {
                string id = m.Groups[1].Value;
                if (id.Contains("%20") || !seen.Add(id)) continue;
                string xml = Http(LgsUrl + "/device/" + id);
                if (xml == null) continue;
                string name = WebUtility.HtmlDecode(Tag(xml, "device_name"));
                string type = Tag(xml, "device_type");
                double pct;
                if (name.Length == 0 || !double.TryParse(Tag(xml, "battery_percent"), NumberStyles.Float, CultureInfo.InvariantCulture, out pct)) continue;

                // G-HUB- und HID-Eintrag desselben Geraets zusammenfassen (Modell = erstes Wort, z.B. G502)
                string key = "logi:" + name.Split(' ')[0].ToUpperInvariant();
                bool isGhub = id.StartsWith("dev", StringComparison.OrdinalIgnoreCase);
                LogiAcc a;
                if (!acc.TryGetValue(key, out a)) { a = new LogiAcc(); acc[key] = a; }
                if (a.Name == null || (!isGhub && a.Name.Length > name.Length)) a.Name = CleanLogiName(name);
                if (a.Type == null) a.Type = type;
                bool valid = pct >= 0;
                if (isGhub)
                {
                    if (valid) { a.GhubPct = (int)Math.Round(pct); a.GhubCharging = Tag(xml, "charging") == "True"; }
                }
                else
                {
                    // direkte HID-Abfrage: LGSTray meldet -1, wenn das Geraet nicht erreichbar ist.
                    // (Das Alter von last_update ist KEIN verlaessliches Zeichen fuer Ruhemodus –
                    //  LGSTray aktualisiert auch bei aktiven Geraeten teils 20+ Minuten nicht.)
                    a.HasNative = true;
                    if (valid) { a.NativePct = (int)Math.Round(pct); a.NativeCharging = Tag(xml, "charging") == "True"; }
                    a.NativeOnline = valid;
                }
            }
            var list = new List<DeviceInfo>();
            foreach (var kv in acc)
            {
                var a = kv.Value;
                var di = new DeviceInfo();
                di.Id = kv.Key;
                di.Name = a.Name;
                di.Source = "Logitech (LGSTray)";
                di.Percent = a.GhubPct ?? a.NativePct;             // G-HUB-Wert bevorzugen (wie in G HUB)
                di.Connected = a.HasNative ? a.NativeOnline : a.GhubPct.HasValue;
                di.Charging = a.GhubPct.HasValue ? a.GhubCharging : a.NativeCharging;
                di.DefaultIcon = a.Type == "Keyboard" ? "Tastatur" : a.Type == "Mouse" ? "Maus" : a.Type == "Headset" ? "Kopfhörer" : "Bluetooth";
                list.Add(di);
            }
            return list;
        }

        class LogiAcc
        {
            public string Name, Type;
            public int? GhubPct, NativePct;
            public bool GhubCharging, NativeCharging, HasNative, NativeOnline;
        }

        static bool TryParseLgsTime(string s, out DateTimeOffset v)
        {
            if (DateTimeOffset.TryParseExact(s, "MM/dd/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out v)) return true;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out v);
        }

        // pollPeriod aus der appsettings.toml des laufenden LGSTray lesen (Standard 600 s)
        static double LgsPollPeriod()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("LGSTray"))
                {
                    string dir = Path.GetDirectoryName(p.MainModule.FileName);
                    string toml = File.ReadAllText(Path.Combine(dir, "appsettings.toml"));
                    var m = Regex.Match(toml, @"(?m)^\s*pollPeriod\s*=\s*(\d+)");
                    if (m.Success) return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return 600;
        }

        // ---------- Bluetooth ueber SetupAPI ----------
        [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public int pid; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int req);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out int type, byte[] buf, int size, out int req, int flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        static DEVPROPKEY Key(string g, int pid) { var k = new DEVPROPKEY(); k.fmtid = new Guid(g); k.pid = pid; return k; }
        static readonly DEVPROPKEY PK_Battery = Key("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
        static readonly DEVPROPKEY PK_Connected = Key("83DA6326-97A6-4088-9453-A1923F573B29", 15);
        static readonly DEVPROPKEY PK_FriendlyName = Key("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        static readonly DEVPROPKEY PK_Name = Key("b725f130-47ef-101a-a5f1-02608c9eebac", 10);

        static byte[] Prop(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key)
        {
            int type, req;
            var buf = new byte[1024];
            if (!SetupDiGetDevicePropertyW(set, ref d, ref key, out type, buf, buf.Length, out req, 0)) return null;
            var r = new byte[req];
            Array.Copy(buf, r, req);
            return r;
        }

        static string PropStr(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key)
        {
            var b = Prop(set, ref d, key);
            if (b == null) return null;
            var s = Encoding.Unicode.GetString(b).TrimEnd('\0');
            return s.Length == 0 ? null : s;
        }

        class BtNode { public string Name; public bool Connected; public bool IsLe; public bool HasMain; public int? Battery; public string BattProfile; }

        static List<DeviceInfo> ReadBluetooth()
        {
            var nodes = new Dictionary<string, BtNode>();
            foreach (string en in new[] { "BTHENUM", "BTHLE" })
            {
                IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, en, IntPtr.Zero, 0x2 | 0x4); // PRESENT | ALLCLASSES
                if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;
                try
                {
                    for (int i = 0; ; i++)
                    {
                        var d = new SP_DEVINFO_DATA();
                        d.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        if (!SetupDiEnumDeviceInfo(set, i, ref d)) break;
                        var sb = new StringBuilder(512);
                        int req;
                        if (!SetupDiGetDeviceInstanceIdW(set, ref d, sb, sb.Capacity, out req)) continue;
                        string iid = sb.ToString().ToUpperInvariant();

                        string mac = null; bool isMain = false;
                        var m = Regex.Match(iid, @"^(BTHENUM|BTHLE)\\DEV_([0-9A-F]{12})\\");
                        if (m.Success) { mac = m.Groups[2].Value; isMain = true; }
                        else
                        {
                            m = Regex.Match(iid, @"[&_]([0-9A-F]{12})_C[0-9A-F]+$");
                            if (m.Success) mac = m.Groups[1].Value;
                        }
                        if (mac == null) continue;

                        BtNode n;
                        if (!nodes.TryGetValue(mac, out n)) { n = new BtNode(); nodes[mac] = n; }
                        if (isMain)
                        {
                            n.HasMain = true;
                            n.IsLe = iid.StartsWith("BTHLE");
                            n.Name = PropStr(set, ref d, PK_FriendlyName) ?? PropStr(set, ref d, PK_Name);
                            var c = Prop(set, ref d, PK_Connected);
                            n.Connected = c != null && c.Length > 0 && c[0] != 0;
                        }
                        var b = Prop(set, ref d, PK_Battery);
                        if (b != null && b.Length > 0 && (isMain || !n.Battery.HasValue))
                        {
                            n.Battery = b[0];
                            if (!isMain) n.BattProfile = iid;
                        }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(set); }
            }

            var list = new List<DeviceInfo>();
            foreach (var kv in nodes)
            {
                var n = kv.Value;
                if (!n.Battery.HasValue || !n.HasMain) continue;
                string name = n.Name ?? ("Bluetooth " + kv.Key);
                var di = new DeviceInfo();
                di.Id = "bt:" + kv.Key;
                di.Name = name;
                di.Source = n.IsLe ? "Bluetooth LE" : "Bluetooth";
                di.Connected = n.Connected;
                di.Percent = n.Battery; // auch getrennt: letzter von Windows gemerkter Wert
                di.DefaultIcon = GuessIcon(name, n.BattProfile);
                list.Add(di);
            }
            return list;
        }

        static string GuessIcon(string name, string profile)
        {
            string ln = name.ToLowerInvariant();
            if (Regex.IsMatch(ln, @"keyboard|tastatur|keys|g915|k\d{3}")) return "Tastatur";
            if (Regex.IsMatch(ln, @"mouse|maus|mx master|g502|anywhere")) return "Maus";
            if (Regex.IsMatch(ln, @"controller|gamepad|xbox|dualsense|dualshock|joy")) return "Controller";
            if (profile != null && profile.Contains("0000111F")) return "Handy";
            if (Regex.IsMatch(ln, @"boost|speaker|lautsprecher|flip|charge|motion|soundbar|boom")) return "Lautsprecher";
            if (profile != null && profile.Contains("0000111E")) return "Kopfhörer";
            return "Bluetooth";
        }
    }

    // ======================= Eingabe-Aktivitaet je Empfaenger =======================
    // Merkt sich NUR den Zeitpunkt der letzten Eingabe pro Geraet/Empfaenger (z.B. "046D:C547"),
    // keine Tasten und keine Mausbewegungen. Damit wird "Beleuchtung aus / unbenutzt" erkannt.
    class InputActivity : NativeWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public int Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public int Type; public int Size; public IntPtr Device; public IntPtr WParam; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint num, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRaw, uint cmd, out RAWINPUTHEADER data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, StringBuilder data, ref uint size);

        const int WM_INPUT = 0x00FF;
        const int RIDEV_INPUTSINK = 0x00000100;
        static readonly DateTime started = DateTime.Now;
        static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
        readonly Dictionary<IntPtr, string> keyCache = new Dictionary<IntPtr, string>();

        public InputActivity()
        {
            CreateHandle(new CreateParams { Caption = "AkkuAnzeigeInput" });
            var devs = new[]
            {
                new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = Handle }, // Maus
                new RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = RIDEV_INPUTSINK, Target = Handle }  // Tastatur
            };
            RegisterRawInputDevices(devs, (uint)devs.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                try
                {
                    RAWINPUTHEADER h;
                    uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
                    if (GetRawInputData(m.LParam, 0x10000005 /*RID_HEADER*/, out h, ref size, size) != unchecked((uint)-1) && h.Device != IntPtr.Zero)
                    {
                        string key;
                        if (!keyCache.TryGetValue(h.Device, out key))
                        {
                            uint len = 512;
                            var sb = new StringBuilder((int)len);
                            GetRawInputDeviceInfo(h.Device, 0x20000007 /*RIDI_DEVICENAME*/, sb, ref len);
                            key = KeyFromPath(sb.ToString());
                            keyCache[h.Device] = key;
                        }
                        if (key != null) lock (last) last[key] = DateTime.Now;
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        // "...VID_046D&PID_C547..." -> "046D:C547"
        public static string KeyFromPath(string path)
        {
            var mm = Regex.Match(path ?? "", @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            return mm.Success ? (mm.Groups[1].Value + ":" + mm.Groups[2].Value).ToUpperInvariant() : null;
        }

        // Sekunden seit der letzten Eingabe (vor der ersten Eingabe: seit Programmstart)
        public static double IdleSeconds(string key)
        {
            DateTime t;
            lock (last) { if (!last.TryGetValue(key, out t)) t = started; }
            return (DateTime.Now - t).TotalSeconds;
        }
    }

    // ======================= G HUB (lokale WebSocket-Schnittstelle, nur lesend) =======================
    static class GHub
    {
        static System.Net.WebSockets.ClientWebSocket ws;
        static int msgNo;

        static bool Ensure()
        {
            if (ws != null && ws.State == System.Net.WebSockets.WebSocketState.Open) return true;
            Reset();
            ws = new System.Net.WebSockets.ClientWebSocket();
            ws.Options.AddSubProtocol("json");
            ws.Options.SetRequestHeader("Origin", "file://");
            try { return ws.ConnectAsync(new Uri("ws://localhost:9010"), CancellationToken.None).Wait(3000) && ws.State == System.Net.WebSockets.WebSocketState.Open; }
            catch { return false; }
        }

        static void Reset()
        {
            try { if (ws != null) ws.Abort(); } catch { }
            try { if (ws != null) ws.Dispose(); } catch { }
            ws = null;
        }

        static string Receive(int timeoutMs)
        {
            var sb = new StringBuilder();
            var buf = new byte[262144];
            System.Net.WebSockets.WebSocketReceiveResult r;
            do
            {
                var t = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                if (!t.Wait(timeoutMs)) throw new TimeoutException();
                r = t.Result;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            } while (!r.EndOfMessage);
            return sb.ToString();
        }

        // GET-Anfrage, liefert die komplette Antwort (msgId/result/payload)
        static Dictionary<string, object> Get(string path)
        {
            string id = "akku" + (++msgNo);
            var req = Encoding.UTF8.GetBytes("{\"msgId\":\"" + id + "\",\"verb\":\"GET\",\"path\":\"" + path + "\"}");
            if (!ws.SendAsync(new ArraySegment<byte>(req), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait(3000))
                throw new TimeoutException();
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            for (int i = 0; i < 30; i++)
            {
                var msg = js.DeserializeObject(Receive(3000)) as Dictionary<string, object>;
                if (msg != null && Str(msg, "msgId") == id) return msg;
            }
            throw new TimeoutException();
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static Dictionary<string, object> Dict(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }

        // Empfaenger aus "path"/"activeInterfaces.path" (z.B. usb\vid_046d&pid_c547&mi_02...) -> "046D:C547"
        static string InputKeyOf(Dictionary<string, object> d)
        {
            string k = InputActivity.KeyFromPath(Str(d, "path"));
            if (k != null) return k;
            object ai;
            if (d.TryGetValue("activeInterfaces", out ai))
            {
                var list = ai is object[] ? (object[])ai : new[] { ai };
                foreach (var o in list)
                {
                    k = InputActivity.KeyFromPath(Str(o as Dictionary<string, object>, "path"));
                    if (k != null) return k;
                }
            }
            return null;
        }

        // null = G HUB nicht erreichbar
        public static List<DeviceInfo> Read()
        {
            if (!Ensure()) return null;
            try
            {
                var list = new List<DeviceInfo>();
                var devs = Dict(Get("/devices/list"), "payload");
                object arr;
                if (devs == null || !devs.TryGetValue("deviceInfos", out arr) || !(arr is object[])) return list;
                foreach (var o in (object[])arr)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string id = Str(d, "id"), name = Str(d, "displayName"), state = Str(d, "state"), type = Str(d, "deviceType");
                    if (id.Length == 0 || name.Length == 0) continue;
                    bool active = state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase); // aus/Ruhe: NOT_CONNECTED
                    var bat = Get("/battery/" + id + "/state");
                    bool ok = Str(Dict(bat, "result"), "code") == "SUCCESS";
                    var p = Dict(bat, "payload");
                    double pct = -1;
                    if (ok) double.TryParse(Str(p, "percentage"), NumberStyles.Float, CultureInfo.InvariantCulture, out pct);
                    if (active && (!ok || pct < 0)) continue; // aktiv, aber ohne Akku (kabelgebunden)

                    var di = new DeviceInfo();
                    di.Id = "logi:" + name.Split(' ')[0].ToUpperInvariant(); // gleiche ID wie bisher (z.B. logi:G502)
                    di.Name = name;
                    di.Source = "Logitech (G HUB)";
                    di.Percent = ok && pct >= 0 ? (int?)(int)Math.Round(pct) : null; // ausgeschaltet: letzter Wert kommt aus dem Speicher
                    di.Charging = ok && Str(p, "charging").Equals("True", StringComparison.OrdinalIgnoreCase);
                    di.Connected = active;
                    di.InputKey = InputKeyOf(d);
                    di.DefaultIcon = type == "KEYBOARD" ? "Tastatur" : type == "MOUSE" ? "Maus" : type == "HEADSET" ? "Kopfhörer" : "Bluetooth";
                    list.Add(di);
                }
                return list;
            }
            catch { Reset(); return null; }
        }
    }

    // ======================= Monitore (auch deaktivierte) =======================
    public class MonitorInfo { public string Id; public string Name; public bool Active; public string GdiName; }

    static class Displays
    {
        [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] struct SRC { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct TGT { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public uint rNum; public uint rDen; public uint scanLine; public int targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] struct PATH { public SRC source; public TGT target; public uint flags; }
        [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODE { public uint infoType; public uint id; public LUID adapterId; }
        [StructLayout(LayoutKind.Sequential)] struct HDR { public uint type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TNAME { public HDR header; public uint flags; public uint outTech; public ushort edidMan; public ushort edidProd; public uint conn; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string friendly; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string path; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SNAME { public HDR header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string gdi; }
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint f, out uint np, out uint nm);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint f, ref uint np, [Out] PATH[] p, ref uint nm, [Out] MODE[] m, IntPtr t);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TNAME r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SNAME r);

        public static int MonNum(string devName)
        {
            var m = Regex.Match(devName, @"(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        public static List<MonitorInfo> GetAll()
        {
            var list = new List<MonitorInfo>();
            try
            {
                uint np, nm;
                if (GetDisplayConfigBufferSizes(1, out np, out nm) != 0) return list; // QDC_ALL_PATHS
                var p = new PATH[np];
                var m = new MODE[nm];
                if (QueryDisplayConfig(1, ref np, p, ref nm, m, IntPtr.Zero) != 0) return list;
                var seen = new HashSet<string>();
                for (int pass = 0; pass < 2; pass++) // erst aktive, dann angeschlossene aber deaktivierte
                {
                    for (int i = 0; i < np; i++)
                    {
                        var x = p[i];
                        bool active = (x.flags & 1) != 0;
                        if (x.target.targetAvailable == 0 || active != (pass == 0)) continue;
                        string key = x.target.adapterId.Low + "/" + x.target.adapterId.High + "/" + x.target.id;
                        if (seen.Contains(key)) continue;
                        var t = new TNAME();
                        t.header.type = 2; t.header.size = (uint)Marshal.SizeOf(typeof(TNAME));
                        t.header.adapterId = x.target.adapterId; t.header.id = x.target.id;
                        if (DisplayConfigGetDeviceInfo(ref t) != 0) continue;
                        seen.Add(key);
                        var mi = new MonitorInfo();
                        mi.Id = string.IsNullOrEmpty(t.path) ? key : t.path.ToUpperInvariant();
                        mi.Name = string.IsNullOrEmpty(t.friendly) ? "Monitor" : t.friendly;
                        mi.Active = active;
                        if (active)
                        {
                            var s = new SNAME();
                            s.header.type = 1; s.header.size = (uint)Marshal.SizeOf(typeof(SNAME));
                            s.header.adapterId = x.source.adapterId; s.header.id = x.source.id;
                            if (DisplayConfigGetDeviceInfo(ref s) == 0) mi.GdiName = s.gdi;
                        }
                        list.Add(mi);
                    }
                }
            }
            catch { }
            return list.OrderBy(x => x.Active ? 0 : 1)
                       .ThenBy(x => x.Active ? Displays.MonNum(x.GdiName ?? "") : 0)
                       .ThenBy(x => x.Name).ToList();
        }
    }

}
