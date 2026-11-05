// Akku-Anzeige: direkter Zugriff auf HID-Geraete (ohne Hersteller-Software), nur lesend/abfragend.
// Kompiliert mit dem .NET Framework csc (C# 5).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AkkuAnzeige
{
    public class HidInfo
    {
        public string Path;
        public ushort Vid, Pid;
        public ushort UsagePage, Usage;
        public int InLen, OutLen, FeatLen; // Berichtslaengen inkl. Report-ID
        public string Product;
        public string Serial;
        // "MI_02" aus dem Pfad (Interface-Nummer), -1 wenn keine
        public int Interface
        {
            get
            {
                int i = Path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
                int v;
                return i >= 0 && Path.Length >= i + 6 && int.TryParse(Path.Substring(i + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out v) ? v : -1;
            }
        }
        public override string ToString() { return string.Format("{0:X4}:{1:X4} up={2:X4} u={3:X4} in={4} out={5} feat={6} {7}", Vid, Pid, UsagePage, Usage, InLen, OutLen, FeatLen, Path); }
    }

    // Geoeffnete HID-Schnittstelle mit Lese-Timeout (overlapped I/O)
    public class HidDevice : IDisposable
    {
        readonly SafeFileHandle h;
        public readonly HidInfo Info;

        HidDevice(SafeFileHandle handle, HidInfo info) { h = handle; Info = info; }

        public static HidDevice Open(HidInfo info)
        {
            var hh = Hid.CreateFile(info.Path, Hid.GENERIC_READ | Hid.GENERIC_WRITE, Hid.FILE_SHARE_READ | Hid.FILE_SHARE_WRITE,
                                    IntPtr.Zero, Hid.OPEN_EXISTING, Hid.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (hh.IsInvalid) { hh.Dispose(); return null; }
            return new HidDevice(hh, info);
        }

        public void Dispose() { try { h.Dispose(); } catch { } }

        // Bericht senden (data[0] = Report-ID). Wird auf die Ausgabelaenge der Schnittstelle aufgefuellt.
        public bool Write(byte[] data, int timeoutMs)
        {
            var buf = new byte[Math.Max(data.Length, Info.OutLen)];
            Array.Copy(data, buf, data.Length);
            return Io(buf, true, timeoutMs) >= 0;
        }

        // naechsten Eingabebericht lesen; null bei Timeout/Fehler
        public byte[] Read(int timeoutMs)
        {
            var buf = new byte[Math.Max(1, Info.InLen)];
            int n = Io(buf, false, timeoutMs);
            if (n <= 0) return null;
            if (n == buf.Length) return buf;
            var r = new byte[n];
            Array.Copy(buf, r, n);
            return r;
        }

        public byte[] GetFeature(byte reportId)
        {
            var buf = new byte[Math.Max(1, Info.FeatLen)];
            buf[0] = reportId;
            return Hid.HidD_GetFeature(h, buf, buf.Length) ? buf : null;
        }

        int Io(byte[] buf, bool write, int timeoutMs)
        {
            using (var evt = new ManualResetEvent(false))
            {
                var ov = new NativeOverlapped();
                ov.EventHandle = evt.SafeWaitHandle.DangerousGetHandle();
                var pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
                IntPtr pov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                try
                {
                    Marshal.StructureToPtr(ov, pov, false);
                    int done;
                    bool ok = write ? Hid.WriteFile(h, pin.AddrOfPinnedObject(), buf.Length, out done, pov)
                                    : Hid.ReadFile(h, pin.AddrOfPinnedObject(), buf.Length, out done, pov);
                    if (!ok)
                    {
                        if (Marshal.GetLastWin32Error() != 997 /*ERROR_IO_PENDING*/) return -1;
                        if (!evt.WaitOne(timeoutMs))
                        {
                            Hid.CancelIoEx(h, pov);
                            Hid.GetOverlappedResult(h, pov, out done, true); // auf Abbruch warten, bevor der Puffer frei wird
                            return -1;
                        }
                        if (!Hid.GetOverlappedResult(h, pov, out done, false)) return -1;
                    }
                    return done;
                }
                finally
                {
                    Marshal.FreeHGlobal(pov);
                    pin.Free();
                }
            }
        }
    }

    public static class Hid
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int len, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int len, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int done, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder sb, int len);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetSerialNumberString(SafeFileHandle h, StringBuilder sb, int len);
        [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);

        [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                          NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                          NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr en, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        // Beschreibungen nach Pfad zwischenspeichern: das Oeffnen und die Produkt-/Seriennummer-Abfrage
        // sind USB-Anfragen ans Geraet und sollen nicht bei jeder Abfrage (alle 5 s) wiederholt werden
        static readonly Dictionary<string, HidInfo> described = new Dictionary<string, HidInfo>(StringComparer.OrdinalIgnoreCase);

        // Alle HID-Schnittstellen eines Herstellers (optional nur bestimmte Produkt-IDs)
        public static List<HidInfo> Enumerate(ushort vendor, params ushort[] pids)
        {
            var res = new List<HidInfo>();
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10); // PRESENT | DEVICEINTERFACE
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return res;
            try
            {
                for (int i = 0; ; i++)
                {
                    var d = new SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    string path;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize von SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, req, out req, IntPtr.Zero)) continue;
                        path = Marshal.PtrToStringUni(buf + 4);
                    }
                    finally { Marshal.FreeHGlobal(buf); }

                    // VID/PID aus dem Pfad vorfiltern (spart das Oeffnen fremder Geraete)
                    var m = System.Text.RegularExpressions.Regex.Match(path, @"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    ushort vid = Convert.ToUInt16(m.Groups[1].Value, 16), pid = Convert.ToUInt16(m.Groups[2].Value, 16);
                    if (vid != vendor || (pids.Length > 0 && Array.IndexOf(pids, pid) < 0)) continue;
                    present.Add(path);
                    HidInfo hi;
                    lock (described)
                    {
                        if (!described.TryGetValue(path, out hi))
                        {
                            hi = Describe(path, vid, pid);
                            if (hi != null) described[path] = hi;
                        }
                    }
                    if (hi != null) res.Add(hi);
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            // abgesteckte Geraete dieses Herstellers vergessen
            lock (described)
                foreach (var k in described.Where(kv => kv.Value.Vid == vendor && (pids.Length == 0 || Array.IndexOf(pids, kv.Value.Pid) >= 0) && !present.Contains(kv.Key)).Select(kv => kv.Key).ToList())
                    described.Remove(k);
            return res;
        }

        // Faehigkeiten lesen, ohne Lese-/Schreibrechte zu brauchen (Zugriff 0)
        static HidInfo Describe(string path, ushort vid, ushort pid)
        {
            using (var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var hi = new HidInfo { Path = path, Vid = vid, Pid = pid };
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (HidD_GetAttributes(h, ref a)) { hi.Vid = a.VendorID; hi.Pid = a.ProductID; }
                IntPtr pp;
                if (HidD_GetPreparsedData(h, out pp))
                {
                    try
                    {
                        HIDP_CAPS c;
                        if (HidP_GetCaps(pp, out c) == 0x00110000 /*HIDP_STATUS_SUCCESS*/)
                        {
                            hi.UsagePage = c.UsagePage; hi.Usage = c.Usage;
                            hi.InLen = c.InputReportByteLength; hi.OutLen = c.OutputReportByteLength; hi.FeatLen = c.FeatureReportByteLength;
                        }
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                var sb = new StringBuilder(128);
                if (HidD_GetProductString(h, sb, sb.Capacity * 2)) hi.Product = sb.ToString();
                sb.Length = 0;
                if (HidD_GetSerialNumberString(h, sb, sb.Capacity * 2)) hi.Serial = sb.ToString();
                return hi;
            }
        }
    }
}
