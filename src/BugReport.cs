// Fehler melden:
//  - "Privat senden": Issue im privaten Projekt Battery-Monitoring-Feedback (ueber ein Bot-Konto),
//    landet in der GitHub-Inbox von Diabeetus und Bobohatt. Kein GitHub-Konto des Meldenden noetig.
//  - "Auf GitHub melden": oeffentliches Issue ueber den Browser (eigenes GitHub-Konto)
//  - "Pull Request erstellen" / "Text kopieren"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AkkuAnzeige
{
    static class PrivateFeedback
    {
        public const string Repo = "Diabeetus94/Battery-Monitoring-Feedback";
        // Benachrichtigt werden: Besitzer + Mitarbeiter des Feedback-Projekts (zusaetzlich per Erwaehnung)
        public const string Notify = "@Diabeetus94 @bobohatt";

        // Schluessel wird beim Bauen verschleiert eingebettet (siehe build.ps1); fehlt er, ist die Funktion aus
        public static string Token()
        {
            var b = IconTools.Resource("AkkuAnzeige.feedback.bin");
            if (b == null || b.Length == 0) return null;
            var key = Encoding.ASCII.GetBytes("AkkuAnzeige-Diabeetus-Bobohatt");
            for (int i = 0; i < b.Length; i++) b[i] ^= key[i % key.Length];
            return Encoding.ASCII.GetString(b).Trim();
        }

        public static bool Available { get { return !string.IsNullOrEmpty(Token()); } }

        // null = gesendet, sonst Fehlertext
        public static string Send(string title, string body)
        {
            string token = Token();
            if (string.IsNullOrEmpty(token)) return "NO_TOKEN";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/issues");
                req.Method = "POST";
                req.UserAgent = "AkkuAnzeige-Feedback";
                req.Accept = "application/vnd.github+json";
                req.ContentType = "application/json; charset=utf-8";
                req.Headers["Authorization"] = "Bearer " + token;
                req.Timeout = 15000;
                string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "title", title }, { "body", body }, { "labels", new[] { "bug" } }
                });
                var data = Encoding.UTF8.GetBytes(json);
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return (int)resp.StatusCode == 201 ? null : "HTTP " + (int)resp.StatusCode;
            }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                if (hr != null && ((int)hr.StatusCode == 401 || (int)hr.StatusCode == 403 || (int)hr.StatusCode == 404)) return "AUTH";
                return we.Message;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    class BugReportForm : Form
    {
        readonly AppCtx ctx;
        TextBox txtTitle, txtBody, txtContact;
        CheckBox chkSys;
        Button btnPrivate;
        Label lblState;

        public BugReportForm(AppCtx c, Icon icon, bool dark)
        {
            ctx = c;
            Text = L.T("Bug melden") + " – Akku-Anzeige";
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 610);
            Icon = icon;

            var lblT = new Label { Text = L.T("Kurzbeschreibung:"), Bounds = new Rectangle(18, 16, 640, 20) };
            txtTitle = new TextBox { Bounds = new Rectangle(18, 38, 644, 26) };
            var lblB = new Label { Text = L.T("Was ist passiert? Was hast du erwartet? Wie lässt es sich nachstellen?"), Bounds = new Rectangle(18, 74, 644, 20) };
            txtBody = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Bounds = new Rectangle(18, 96, 644, 210) };
            var lblC = new Label { Text = L.T("Kontakt für Rückfragen (optional, z. B. Discord oder E-Mail):"), Bounds = new Rectangle(18, 316, 644, 20) };
            txtContact = new TextBox { Bounds = new Rectangle(18, 338, 644, 26) };
            chkSys = new CheckBox { Text = L.T("Systeminfos anhängen (Version, Windows, Datenquellen, letzte Fehlermeldungen – ohne Gerätenamen)"), Checked = true, Bounds = new Rectangle(18, 374, 644, 24) };
            var lnkShow = new LinkLabel { Text = L.T("Systeminfos ansehen"), Bounds = new Rectangle(36, 400, 300, 20) };
            lnkShow.LinkClicked += delegate { MessageBox.Show(this, SystemInfo(), L.T("Systeminfos"), MessageBoxButtons.OK, MessageBoxIcon.Information); };
            var lblHint = new Label
            {
                Text = L.T("„Privat senden“ schickt die Meldung direkt und nur an die Entwickler (Diabeetus & Bobohatt) – kein GitHub-Konto nötig. „Auf GitHub melden“ erstellt eine öffentliche Meldung mit deinem GitHub-Konto. „Pull Request“ ist für Code-Korrekturen."),
                Bounds = new Rectangle(18, 428, 644, 60), ForeColor = Color.DimGray, UseMnemonic = false
            };
            lblState = new Label { Bounds = new Rectangle(18, 494, 644, 40), UseMnemonic = false };

            btnPrivate = new Button { Text = "🔒 " + L.T("Privat senden"), Bounds = new Rectangle(18, 556, 140, 36), Enabled = PrivateFeedback.Available };
            var btnIssue = new Button { Text = L.T("Auf GitHub melden"), Bounds = new Rectangle(164, 556, 150, 36) };
            var btnPr = new Button { Text = L.T("Pull Request erstellen"), Bounds = new Rectangle(320, 556, 150, 36) };
            var btnCopy = new Button { Text = L.T("Text kopieren"), Bounds = new Rectangle(476, 556, 96, 36) };
            var btnClose = new Button { Text = L.T("Abbrechen"), Bounds = new Rectangle(578, 556, 84, 36) };
            if (!PrivateFeedback.Available) lblState.Text = L.T("„Privat senden“ ist in dieser Version nicht verfügbar.");

            btnPrivate.Click += delegate { SendPrivate(); };
            btnIssue.Click += delegate { if (Check()) { UpdateUi.Open(IssueUrl()); Close(); } };
            btnPr.Click += delegate
            {
                try { Clipboard.SetText(Report(true)); } catch { }
                UpdateUi.Open("https://github.com/" + AppInfo.GitHubRepo + "/compare");
                MessageBox.Show(this, L.T("Die Beschreibung ist in der Zwischenablage – füge sie beim Erstellen des Pull Requests ein."), "Akku-Anzeige", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            btnCopy.Click += delegate
            {
                try { Clipboard.SetText(Report(true)); MessageBox.Show(this, L.T("Der Text ist in der Zwischenablage."), "Akku-Anzeige", MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            };
            btnClose.Click += delegate { Close(); };
            CancelButton = btnClose;
            Controls.AddRange(new Control[] { lblT, txtTitle, lblB, txtBody, lblC, txtContact, chkSys, lnkShow, lblHint, lblState, btnPrivate, btnIssue, btnPr, btnCopy, btnClose });
            HandleCreated += delegate { Theme.Apply(this, dark); lnkShow.LinkColor = Theme.Accent(dark); lnkShow.ActiveLinkColor = Theme.Accent(dark); };
        }

        bool Check()
        {
            if (txtTitle.Text.Trim().Length == 0 && txtBody.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, L.T("Bitte beschreibe kurz, was passiert ist."), "Akku-Anzeige", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        string Title()
        {
            string title = txtTitle.Text.Trim();
            if (title.Length == 0) title = txtBody.Text.Trim().Split('\n')[0].Trim();
            if (title.Length > 120) title = title.Substring(0, 117) + "...";
            return "[Bug] " + title;
        }

        void SendPrivate()
        {
            if (!Check()) return;
            btnPrivate.Enabled = false;
            lblState.Text = L.T("Sende Meldung …");
            string title = Title();
            var body = new StringBuilder(Report(false));
            if (txtContact.Text.Trim().Length > 0) body.AppendLine().AppendLine("**" + L.T("Kontakt") + ":** " + txtContact.Text.Trim());
            body.AppendLine().AppendLine("---").AppendLine("Gesendet aus Akku-Anzeige " + AppInfo.VersionText + " · cc " + PrivateFeedback.Notify);
            string text = body.ToString();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = PrivateFeedback.Send(title, text);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (err == null)
                        {
                            MessageBox.Show(this, L.T("Danke! Deine Meldung wurde privat an die Entwickler gesendet."), "Akku-Anzeige", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            Close();
                            return;
                        }
                        btnPrivate.Enabled = true;
                        lblState.Text = err == "AUTH"
                            ? L.T("Privates Senden ist gerade nicht möglich (Zugang abgelaufen). Bitte „Auf GitHub melden“ oder „Text kopieren“ nutzen.")
                            : L.F("Senden fehlgeschlagen: {0}", err);
                    }));
                }
                catch { }
            });
        }

        string Report(bool withTitle)
        {
            var sb = new StringBuilder();
            if (withTitle && txtTitle.Text.Trim().Length > 0) sb.AppendLine("## " + txtTitle.Text.Trim()).AppendLine();
            sb.AppendLine(txtBody.Text.Trim());
            if (chkSys.Checked)
            {
                sb.AppendLine().AppendLine("---").AppendLine("<details><summary>" + L.T("Systeminfos") + "</summary>").AppendLine().AppendLine("```");
                sb.AppendLine(SystemInfo()).AppendLine("```").AppendLine("</details>");
            }
            return sb.ToString();
        }

        string IssueUrl()
        {
            string body = Report(false);
            if (body.Length > 6000) body = body.Substring(0, 6000) + "\n\n" + L.T("(gekürzt)");
            return "https://github.com/" + AppInfo.GitHubRepo + "/issues/new?labels=bug&title=" +
                   Uri.EscapeDataString(Title()) + "&body=" + Uri.EscapeDataString(body);
        }

        // Systeminfos ohne persoenliche Daten (keine Geraetenamen, Benutzerpfade anonymisiert)
        string SystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Akku-Anzeige " + AppInfo.VersionText);
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    sb.AppendLine("Windows: " + k.GetValue("ProductName") + " " + k.GetValue("DisplayVersion") + " (Build " + k.GetValue("CurrentBuild") + "." + k.GetValue("UBR") + ")");
            }
            catch { sb.AppendLine("Windows: " + Environment.OSVersion.VersionString); }
            sb.AppendLine(".NET: " + Environment.Version + " · " + (Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit"));
            sb.AppendLine("Sprache: " + L.Current + " · Monitore: " + Screen.AllScreens.Length);
            try
            {
                var keys = Links.BuiltIns.Select(b => b.Key).Concat(new[] { "ghub", "lgstray" }).Distinct();
                sb.AppendLine("Datenquellen: " + string.Join(", ", keys.Select(k => k + (Links.Enabled(k) ? "=an" : "=aus"))));
            }
            catch { }
            try { sb.AppendLine("Erkannte Software: " + string.Join(", ", Vendors.Detect(true).Select(v => v.Name + (string.IsNullOrEmpty(v.Version) ? "" : " " + v.Version)))); } catch { }
            try
            {
                var groups = ctx.Available.GroupBy(d => d.Source).Select(g => g.Key + ": " + g.Count() + " (" + g.Count(d => d.Connected) + " verbunden)");
                sb.AppendLine("Geräte: " + string.Join(", ", groups));
            }
            catch { }
            try
            {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige", "error.log");
                if (File.Exists(log))
                {
                    var lines = File.ReadAllLines(log, Encoding.UTF8).Reverse().Take(15).Reverse().ToArray();
                    if (lines.Length > 0)
                    {
                        string prof = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        sb.AppendLine("Letzte Fehler:");
                        foreach (var l in lines)
                            sb.AppendLine("  " + l.Replace(prof, "%USERPROFILE%").Replace(Environment.UserName, "<user>"));
                    }
                }
            }
            catch { }
            return sb.ToString().TrimEnd();
        }
    }
}
