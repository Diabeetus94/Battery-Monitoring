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

        public static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string VersionText { get { var v = Version; return v.Major + "." + v.Minor + "." + v.Build + (Stage.Length > 0 ? " " + Stage : ""); } }
        public static string Website { get { return GitHubRepo.Length > 0 ? "https://github.com/" + GitHubRepo : null; } }
    }

    // Fragt GitHub nach dem neuesten Release (Tag z.B. "v1.3.0")
    public static class UpdateChecker
    {
        public class Result { public bool Ok; public bool NewerAvailable; public string Latest; public string Url; public string Error; }

        public static Result Check()
        {
            var r = new Result();
            if (AppInfo.GitHubRepo.Length == 0) { r.Error = "NO_REPO"; return r; }
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Liste statt /latest, damit auch Vorab-Versionen (Alpha/Beta) gefunden werden
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + AppInfo.GitHubRepo + "/releases?per_page=5");
                req.UserAgent = "AkkuAnzeige-UpdateCheck";
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000;
                string json;
                try
                {
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
                }
                catch (WebException we)
                {
                    var hr = we.Response as HttpWebResponse;
                    if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) { r.Error = "PRIVATE"; return r; } // privat oder nicht vorhanden
                    throw;
                }
                var arr = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as object[];
                if (arr == null || arr.Length == 0) { r.Error = "PRIVATE"; return r; }
                var d = arr.Cast<object>().OfType<System.Collections.Generic.Dictionary<string, object>>()
                           .FirstOrDefault(x => !(x.ContainsKey("draft") && Convert.ToBoolean(x["draft"])));
                string tag = d != null && d.ContainsKey("tag_name") ? Convert.ToString(d["tag_name"]) : "";
                r.Url = d != null && d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : AppInfo.Website;
                var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
                if (!m.Success) { r.Error = "Tag '" + tag + "'"; return r; }
                var latest = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
                var cur = AppInfo.Version;
                r.Latest = latest.Major + "." + latest.Minor + "." + latest.Build;
                r.NewerAvailable = latest > new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
                r.Ok = true;
            }
            catch (Exception ex) { r.Error = ex.Message; }
            return r;
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
