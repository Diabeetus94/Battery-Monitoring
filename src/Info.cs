// Info-Fenster: Version, Hersteller, Datenquellen / erkannte Hersteller-Software, Update-Suche
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AkkuAnzeige
{
    public static class AppInfo
    {
        public const string Publisher = "Diabeetus";
        public const string PoweredBy = "Powered by Claude.ai";
        public const string Year = "2026";

        // GitHub-Projekt im Format "besitzer/repo"
        public const string GitHubRepo = "Diabeetus94/Battery-Monitoring";
        public const string Stage = "Alpha"; // leer lassen fuer eine fertige Version

        // Update-Info (geheimer, nicht gelisteter Gist – auch lesbar, solange das Projekt privat ist)
        public const string UpdateManifestUrl = "https://gist.githubusercontent.com/Diabeetus94/7de64f002a597e3182acb41eb0c31912/raw/akku-anzeige-update.json";

        public static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string Format(Version v) { return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build) + (v.Revision > 0 ? "." + v.Revision : ""); }
        public static string VersionText { get { return Format(Version) + (Stage.Length > 0 ? " " + Stage : ""); } }
        public static string Website { get { return GitHubRepo.Length > 0 ? "https://github.com/" + GitHubRepo : null; } }
    }

    // Sucht nach einer neueren Version: zuerst in der Update-Info (Gist), sonst in den GitHub-Releases
    public static class UpdateChecker
    {
        public class Result
        {
            public bool Ok, NewerAvailable;
            public string Latest, Url, Error;
            public string Download;   // direkter Link zur Setup-Datei
            public string Sha256;     // erwartete Pruefsumme der Setup-Datei (optional)
            public Version LatestVersion;
        }

        static string Get(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks); // ohne Zwischenspeicher
            req.UserAgent = "AkkuAnzeige-UpdateCheck";
            if (accept != null) req.Accept = accept;
            req.Timeout = 8000;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream())) return sr.ReadToEnd();
        }

        // "v1.2.0.1-alpha" / "1.2.0.1" -> Version (fehlende Teile = 0)
        static Version Parse(string s)
        {
            var m = Regex.Match(s ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                               m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0, m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        }

        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        public static Result Check()
        {
            var r = new Result();
            Version latest = null;
            string stage = "";
            var js = new System.Web.Script.Serialization.JavaScriptSerializer();

            // 1) Update-Info (Gist)
            try
            {
                var d = js.DeserializeObject(Get(AppInfo.UpdateManifestUrl, null)) as System.Collections.Generic.Dictionary<string, object>;
                if (d != null && d.ContainsKey("version"))
                {
                    latest = Parse(Convert.ToString(d["version"]));
                    stage = d.ContainsKey("stage") ? Convert.ToString(d["stage"]) : "";
                    r.Url = d.ContainsKey("url") ? Convert.ToString(d["url"]) : AppInfo.Website;
                    if (d.ContainsKey("download")) r.Download = Convert.ToString(d["download"]);
                    if (d.ContainsKey("sha256")) r.Sha256 = Convert.ToString(d["sha256"]);
                }
            }
            catch { latest = null; }

            // 2) Ersatz: GitHub-Releases (nur bei oeffentlichem Projekt)
            if (latest == null)
            {
                try
                {
                    var arr = js.DeserializeObject(Get("https://api.github.com/repos/" + AppInfo.GitHubRepo + "/releases?per_page=5", "application/vnd.github+json")) as object[];
                    var d = arr == null ? null : arr.OfType<System.Collections.Generic.Dictionary<string, object>>()
                                                    .FirstOrDefault(x => !(x.ContainsKey("draft") && Convert.ToBoolean(x["draft"])));
                    if (d == null) { r.Error = "PRIVATE"; return r; }
                    latest = Parse(Convert.ToString(d["tag_name"]));
                    r.Url = d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : AppInfo.Website;
                    // Setup-Datei unter den Anhaengen der Veroeffentlichung
                    var assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
                    if (assets != null)
                        foreach (var a in assets.OfType<System.Collections.Generic.Dictionary<string, object>>())
                        {
                            string n = a.ContainsKey("name") ? Convert.ToString(a["name"]) : "";
                            if (n.StartsWith("AkkuAnzeige-Setup", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            { r.Download = Convert.ToString(a["browser_download_url"]); break; }
                        }
                }
                catch (WebException we)
                {
                    var hr = we.Response as HttpWebResponse;
                    r.Error = hr != null && hr.StatusCode == HttpStatusCode.NotFound ? "PRIVATE" : we.Message;
                    return r;
                }
                catch (Exception ex) { r.Error = ex.Message; return r; }
            }
            if (latest == null) { r.Error = "?"; return r; }

            r.LatestVersion = latest;
            r.Latest = AppInfo.Format(latest) + (stage.Length > 0 ? " " + stage : "");
            r.NewerAvailable = Normalize(latest) > Normalize(AppInfo.Version);
            r.Ok = true;
            return r;
        }
    }

    // Update-Suche mit Anzeige (Info-Bereich im Reiter Einstellungen)
    public static class UpdateUi
    {
        public static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        public static void OpenWebsite(Label status)
        {
            if (AppInfo.Website != null) Open(AppInfo.Website);
            else status.Text = L.T("Es ist noch keine Update-Adresse (GitHub) eingerichtet.");
        }

        public static void Check(Control owner, Button btn, Label status) { Check(owner, btn, status, null, false, null); }

        public static void Check(Control owner, Button btn, Label status, Icon icon, bool dark, Action quitApp)
        {
            btn.Enabled = false;
            status.Text = L.T("Suche nach Updates …");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var r = UpdateChecker.Check();
                try
                {
                    owner.BeginInvoke((Action)(() =>
                    {
                        btn.Enabled = true;
                        if (r.Error == "PRIVATE") status.Text = L.T("Das Projekt ist privat oder hat noch keine Veröffentlichung – die automatische Update-Suche ist erst möglich, wenn es öffentlich ist.");
                        else if (!r.Ok) status.Text = L.F("Update-Suche fehlgeschlagen: {0}", r.Error);
                        else if (!r.NewerAvailable) status.Text = L.F("Du hast die neueste Version ({0}).", AppInfo.VersionText);
                        else
                        {
                            status.Text = L.F("Update verfügbar: Version {0} (installiert: {1}).", r.Latest, AppInfo.VersionText);
                            // Update-Fenster: Changelog + "Jetzt aktualisieren" (Download und Installation in der App)
                            using (var dlg = new UpdateDialog(r, icon ?? owner.FindForm().Icon, dark, quitApp)) dlg.ShowDialog(owner.FindForm());
                        }
                    }));
                }
                catch { }
            });
        }
    }

    // Laedt ein Bild aus einer .ico-Datei – auch PNG-komprimierte Groessen (kann System.Drawing.Icon nicht)
    public static class IconTools
    {
        public static Bitmap FromIco(byte[] ico, int size)
        {
            try
            {
                int count = BitConverter.ToUInt16(ico, 4);
                int best = -1, bestSize = 0;
                for (int i = 0; i < count; i++)
                {
                    int w = ico[6 + 16 * i]; if (w == 0) w = 256;
                    // kleinste Groesse >= gewuenscht, sonst die groesste
                    if (best < 0 || (w >= size && (bestSize < size || w < bestSize)) || (bestSize < size && w > bestSize)) { best = i; bestSize = w; }
                }
                int len = BitConverter.ToInt32(ico, 6 + 16 * best + 8);
                int off = BitConverter.ToInt32(ico, 6 + 16 * best + 12);
                bool png = ico[off] == 0x89 && ico[off + 1] == 0x50 && ico[off + 2] == 0x4E && ico[off + 3] == 0x47;
                Bitmap src;
                if (png) src = new Bitmap(new MemoryStream(ico, off, len));
                else using (var ic = new Icon(new MemoryStream(ico), bestSize, bestSize)) src = ic.ToBitmap();
                var res = new Bitmap(size, size);
                using (var g = Graphics.FromImage(res))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, size, size);
                }
                src.Dispose();
                return res;
            }
            catch { return null; }
        }

        public static byte[] Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }
    }

    class InfoForm : Form
    {
        Label lblUpdate;
        Button btnUpdate;

        public InfoForm(AppCtx ctx, bool dark)
        {
            Text = L.T("Über Akku-Anzeige");
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 520);
            Icon = ctx.WindowIcon;

            var pic = new PictureBox { Bounds = new Rectangle(20, 20, 64, 64), SizeMode = PictureBoxSizeMode.Zoom };
            var icoBytes = IconTools.Resource("AkkuAnzeige.win.ico");
            if (icoBytes != null) pic.Image = IconTools.FromIco(icoBytes, 64);
            var lblName = new Label { Text = "Akku-Anzeige", Font = new Font("Segoe UI Semibold", 16f), Bounds = new Rectangle(98, 18, 440, 34) };
            var lblVer = new Label { Text = L.F("Version {0}", AppInfo.VersionText), Bounds = new Rectangle(100, 54, 440, 20) };
            var lblPub = new Label { Text = L.F("Hersteller: {0}", AppInfo.Publisher) + "   ·   " + AppInfo.PoweredBy, Bounds = new Rectangle(100, 76, 440, 20) };
            var lblCopy = new Label { Text = "© " + AppInfo.Year + " " + AppInfo.Publisher, Bounds = new Rectangle(100, 98, 440, 20), ForeColor = Color.DimGray };
            var lblDesc = new Label { Text = L.T("Akkustände von Maus, Tastatur, Kopfhörern und weiteren drahtlosen Geräten direkt auf der Taskleiste."), Bounds = new Rectangle(20, 132, 520, 36) };

            var gSrc = new GroupBox { Text = L.T("Datenquellen") + " / " + L.T("Erkannte Hersteller-Software"), Bounds = new Rectangle(14, 172, 532, 240) };
            // einfache Zeilen statt ListView (zuverlaessig in hell und dunkel)
            var list = new Panel { Bounds = new Rectangle(10, 22, 512, 208), AutoScroll = true };
            bool en = L.Current == "en";
            int y = 4;
            Action<string, string, string> row = (name, status, note) =>
            {
                list.Controls.Add(new Label { Text = name, Bounds = new Rectangle(4, y, 250, 18), Font = new Font("Segoe UI Semibold", 9f), AutoEllipsis = true });
                list.Controls.Add(new Label { Text = status, Bounds = new Rectangle(258, y, 240, 18), AutoEllipsis = true });
                list.Controls.Add(new Label { Text = note, Bounds = new Rectangle(4, y + 18, 490, 18), ForeColor = Color.DimGray, AutoEllipsis = true });
                y += 44;
            };
            foreach (var b in Links.BuiltIns)
                row(en ? b.NameEn : b.NameDe, Links.Enabled(b.Key) ? L.T("verknüpft") : L.T("nicht verknüpft"), en ? b.NoteEn : b.NoteDe);
            foreach (var v in Vendors.Detect(true))
            {
                bool ok = v.Support == VendorSupport.Supported;
                string st = (v.Running ? L.T("läuft") : L.T("installiert")) + " · " +
                            (ok ? (Links.Enabled(v.Key) ? L.T("verknüpft") : L.T("nicht verknüpft")) : L.T("nicht kompatibel"));
                row(v.Name + (string.IsNullOrEmpty(v.Version) ? "" : " " + v.Version), st, en ? v.NoteEn : v.NoteDe);
            }
            gSrc.Controls.Add(list);

            btnUpdate = new Button { Text = L.T("Nach Updates suchen"), Bounds = new Rectangle(20, 428, 170, 32) };
            var btnWeb = new Button { Text = L.T("Website öffnen"), Bounds = new Rectangle(198, 428, 150, 32) };
            var btnClose = new Button { Text = L.T("Schließen"), Bounds = new Rectangle(446, 474, 100, 32) };
            lblUpdate = new Label { Bounds = new Rectangle(20, 466, 410, 44) };
            btnUpdate.Click += delegate { CheckUpdates(); };
            btnWeb.Click += delegate
            {
                if (AppInfo.Website != null) Open(AppInfo.Website);
                else lblUpdate.Text = L.T("Es ist noch keine Update-Adresse (GitHub) eingerichtet.");
            };
            btnClose.Click += delegate { Close(); };
            CancelButton = btnClose;

            Controls.AddRange(new Control[] { pic, lblName, lblVer, lblPub, lblCopy, lblDesc, gSrc, btnUpdate, btnWeb, lblUpdate, btnClose });
            HandleCreated += delegate { Theme.Apply(this, dark); };
        }

        static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        void CheckUpdates()
        {
            btnUpdate.Enabled = false;
            lblUpdate.Text = L.T("Suche nach Updates …");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var r = UpdateChecker.Check();
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        btnUpdate.Enabled = true;
                        if (r.Error == "NO_REPO") lblUpdate.Text = L.T("Es ist noch keine Update-Adresse (GitHub) eingerichtet.");
                        else if (r.Error == "PRIVATE") lblUpdate.Text = L.T("Das Projekt ist privat oder hat noch keine Veröffentlichung – die automatische Update-Suche ist erst möglich, wenn es öffentlich ist.");
                        else if (!r.Ok) lblUpdate.Text = L.F("Update-Suche fehlgeschlagen: {0}", r.Error);
                        else if (!r.NewerAvailable) lblUpdate.Text = L.F("Du hast die neueste Version ({0}).", AppInfo.VersionText);
                        else
                        {
                            lblUpdate.Text = L.F("Update verfügbar: Version {0} (installiert: {1}).", r.Latest, AppInfo.VersionText);
                            if (MessageBox.Show(this, lblUpdate.Text + "\n\n" + L.T("Jetzt herunterladen?"), L.T("Update verfügbar"),
                                                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                                Open(r.Url ?? AppInfo.Website);
                        }
                    }));
                }
                catch { }
            });
        }
    }
}
