// Automatisches Update (Download + Pruefung + stille Installation) und Changelog-Anzeige
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AkkuAnzeige
{
    // ---------------- Changelog ----------------
    public static class Changelog
    {
        static string FileName { get { return L.Current == "de" ? "CHANGELOG.de.md" : "CHANGELOG.md"; } }

        // aktuelles Changelog von GitHub, sonst das in der App mitgelieferte
        public static string Load()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://raw.githubusercontent.com/" + AppInfo.GitHubRepo + "/main/" + FileName + "?t=" + DateTime.UtcNow.Ticks);
                req.UserAgent = "AkkuAnzeige-UpdateCheck";
                req.Timeout = 6000;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string s = sr.ReadToEnd();
                    if (s.Contains("##")) return s;
                }
            }
            catch { }
            var b = IconTools.Resource("AkkuAnzeige." + FileName);
            return b != null ? Encoding.UTF8.GetString(b) : "";
        }

        // nur der Abschnitt einer Version ("## 1.2.0.2 ...")
        public static string Section(string md, Version v)
        {
            if (string.IsNullOrEmpty(md) || v == null) return "";
            string ver = AppInfo.Format(v);
            var m = Regex.Match(md, @"(?ms)^##\s+" + Regex.Escape(ver) + @"\b.*?(?=^##\s|\z)");
            return m.Success ? m.Value.Trim() : "";
        }

        // einfache Markdown-Darstellung als Text
        public static string ToText(string md)
        {
            var sb = new StringBuilder();
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                string l = raw.TrimEnd();
                if (l.StartsWith("# ")) continue; // Titel
                if (l.StartsWith("## ")) { if (sb.Length > 0) sb.AppendLine(); sb.AppendLine("■ " + l.Substring(3)); continue; }
                if (l.StartsWith("- ")) l = "   • " + l.Substring(2);
                l = l.Replace("**", "").Replace("`", "");
                sb.AppendLine(l);
            }
            // hoechstens eine Leerzeile zwischen den Versionen
            return Regex.Replace(sb.ToString(), @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine).Trim();
        }
    }

    class ChangelogForm : Form
    {
        public ChangelogForm(Icon icon, bool dark)
        {
            Text = "Changelog – Akku-Anzeige";
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 520);
            Icon = icon;
            var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                                    Bounds = new Rectangle(16, 16, 588, 444), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                                    Text = L.T("Changelog wird geladen …") };
            var btnGit = new Button { Text = L.T("Auf GitHub ansehen"), Bounds = new Rectangle(16, 476, 170, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            var btnClose = new Button { Text = L.T("Schließen"), Bounds = new Rectangle(504, 476, 100, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnGit.Click += delegate { UpdateUi.Open("https://github.com/" + AppInfo.GitHubRepo + "/blob/main/" + (L.Current == "de" ? "CHANGELOG.de.md" : "CHANGELOG.md")); };
            btnClose.Click += delegate { Close(); };
            CancelButton = btnClose;
            Controls.AddRange(new Control[] { txt, btnGit, btnClose });
            HandleCreated += delegate { Theme.Apply(this, dark); txt.BackColor = Theme.Page(dark); txt.ForeColor = Theme.Fore(dark); };
            Shown += delegate
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string t = Changelog.ToText(Changelog.Load());
                    try { BeginInvoke((Action)(() => { txt.Text = t.Length > 0 ? t : L.T("Changelog konnte nicht geladen werden."); txt.Select(0, 0); })); } catch { }
                });
            };
        }
    }

    // ---------------- Update herunterladen, pruefen, installieren ----------------
    public static class Updater
    {
        // Nur Updates mit genau diesem Zertifikat werden ausgefuehrt (Akku-Anzeige Code Signing)
        const string ExpectedThumbprint = "127BCB724984E954A924F31A1FAB873DE9F84C20";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class WINTRUST_FILE_INFO
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            public string pcwszFilePath;
            public IntPtr hFile = IntPtr.Zero;
            public IntPtr pgKnownSubject = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        class WINTRUST_DATA
        {
            public uint cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            public IntPtr pPolicyCallbackData = IntPtr.Zero;
            public IntPtr pSIPClientData = IntPtr.Zero;
            public uint dwUIChoice = 2;            // WTD_UI_NONE
            public uint fdwRevocationChecks = 0;   // WTD_REVOKE_NONE
            public uint dwUnionChoice = 1;         // WTD_CHOICE_FILE
            public IntPtr pFile;
            public uint dwStateAction = 0;
            public IntPtr hWVTStateData = IntPtr.Zero;
            public IntPtr pwszURLReference = IntPtr.Zero;
            public uint dwProvFlags = 0x00001000;  // WTD_CACHE_ONLY_URL_RETRIEVAL (keine Netzabfrage)
            public uint dwUIContext = 0;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, WINTRUST_DATA data);

        static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        // null = in Ordnung, sonst Fehlertext
        public static string Verify(string file, string expectedSha256)
        {
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(file))
                {
                    string got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                    if (!got.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)) return L.T("Prüfsumme stimmt nicht.");
                }
            }
            var fi = new WINTRUST_FILE_INFO { pcwszFilePath = file };
            var data = new WINTRUST_DATA();
            data.pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            try
            {
                Marshal.StructureToPtr(fi, data.pFile, false);
                int rc = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
                if (rc != 0) return L.F("Signatur ungültig (0x{0:X8}).", rc);
            }
            finally { Marshal.FreeHGlobal(data.pFile); }
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
                if (!cert.Thumbprint.Equals(ExpectedThumbprint, StringComparison.OrdinalIgnoreCase)) return L.T("Unbekannter Herausgeber.");
            }
            catch { return L.T("Keine Signatur gefunden."); }
            return null;
        }
    }

    // Fenster: neue Version + Changelog, "Jetzt aktualisieren" -> Download, Pruefung, Installation
    class UpdateDialog : Form
    {
        readonly UpdateChecker.Result info;
        readonly Action quitApp;
        ProgressBar bar;
        Label lblState;
        Button btnUpdate, btnLater;
        TextBox txt;
        WebClient wc;
        string tmpFile;

        public UpdateDialog(UpdateChecker.Result r, Icon icon, bool dark, Action quit)
        {
            info = r; quitApp = quit;
            Text = L.T("Update verfügbar");
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            Icon = icon;

            var lblHead = new Label { Text = L.F("Version {0} ist verfügbar", r.Latest), Font = new Font("Segoe UI Semibold", 13f), Bounds = new Rectangle(18, 14, 520, 28) };
            var lblCur = new Label { Text = L.F("Installiert: {0}", AppInfo.VersionText), Bounds = new Rectangle(20, 44, 520, 20), ForeColor = Color.DimGray };
            var lblNew = new Label { Text = L.T("Was ist neu:"), Bounds = new Rectangle(20, 72, 520, 20) };
            txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Bounds = new Rectangle(20, 94, 520, 210), Text = L.T("Changelog wird geladen …") };
            bar = new ProgressBar { Bounds = new Rectangle(20, 318, 520, 16), Visible = false };
            lblState = new Label { Bounds = new Rectangle(20, 340, 520, 36) };
            btnUpdate = new Button { Text = L.T("Update installieren"), Bounds = new Rectangle(290, 384, 140, 32) };
            btnLater = new Button { Text = L.T("Später"), Bounds = new Rectangle(440, 384, 100, 32) };
            btnUpdate.Click += delegate { StartDownload(); };
            btnLater.Click += delegate { Close(); };
            CancelButton = btnLater;
            Controls.AddRange(new Control[] { lblHead, lblCur, lblNew, txt, bar, lblState, btnUpdate, btnLater });
            if (string.IsNullOrEmpty(r.Download)) { btnUpdate.Text = L.T("Download-Seite öffnen"); }

            HandleCreated += delegate { Theme.Apply(this, dark); txt.BackColor = Theme.Page(dark); txt.ForeColor = Theme.Fore(dark); };
            Shown += delegate
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string sec = Changelog.ToText(Changelog.Section(Changelog.Load(), r.LatestVersion));
                    try { BeginInvoke((Action)(() => { txt.Text = sec.Length > 0 ? sec : L.T("Für diese Version ist kein Changelog vorhanden."); txt.Select(0, 0); })); } catch { }
                });
            };
            FormClosing += (s, e) => { if (wc != null && wc.IsBusy) wc.CancelAsync(); };
        }

        void StartDownload()
        {
            if (string.IsNullOrEmpty(info.Download)) { UpdateUi.Open(info.Url ?? AppInfo.Website); Close(); return; }
            btnUpdate.Enabled = false;
            bar.Visible = true; bar.Value = 0;
            lblState.Text = L.T("Lade Update herunter …");
            string dir = Path.Combine(Path.GetTempPath(), "AkkuAnzeige-Update");
            Directory.CreateDirectory(dir);
            tmpFile = Path.Combine(dir, "AkkuAnzeige-Setup-" + AppInfo.Format(info.LatestVersion) + ".exe");
            try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "AkkuAnzeige-Updater";
            wc.DownloadProgressChanged += (s, e) => { bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage)); lblState.Text = L.F("Lade Update herunter … {0} %", e.ProgressPercentage); };
            wc.DownloadFileCompleted += (s, e) => OnDownloaded(e);
            wc.DownloadFileAsync(new Uri(info.Download), tmpFile);
        }

        void OnDownloaded(AsyncCompletedEventArgs e)
        {
            if (e.Cancelled) return;
            if (e.Error != null) { Fail(L.F("Download fehlgeschlagen: {0}", e.Error.Message)); return; }
            lblState.Text = L.T("Prüfe Signatur …");
            string err = Updater.Verify(tmpFile, info.Sha256);
            if (err != null)
            {
                try { File.Delete(tmpFile); } catch { }
                Fail(L.T("Update abgebrochen – die Datei ist nicht vertrauenswürdig:") + " " + err);
                return;
            }
            lblState.Text = L.T("Installiere Update … (bitte die Windows-Abfrage bestätigen)");
            try
            {
                // Setup im stillen Update-Modus; es beendet die App, installiert und startet die neue Version
                Process.Start(new ProcessStartInfo(tmpFile, "/update") { UseShellExecute = true });
                Close();
                if (quitApp != null) quitApp();
            }
            catch (Win32Exception w) { Fail(w.NativeErrorCode == 1223 ? L.T("Installation abgebrochen.") : w.Message); } // 1223 = UAC abgelehnt
            catch (Exception ex) { Fail(ex.Message); }
        }

        void Fail(string msg)
        {
            lblState.Text = msg;
            bar.Visible = false;
            btnUpdate.Enabled = true;
        }
    }
}
