// Bug melden: kleines Fenster mit zwei Wegen zu GitHub – Issue erstellen oder Pull Request erstellen.
// Ausgefuellt wird alles direkt auf GitHub (mit dem eigenen GitHub-Konto).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AkkuAnzeige
{
    class BugReportForm : Form
    {
        public BugReportForm(Icon icon, bool dark)
        {
            Text = L.T("Bug melden") + " – Akku-Anzeige";
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 250);
            Icon = icon;

            var lblHead = new Label { Text = L.T("Wie möchtest du etwas melden?"), Font = new Font("Segoe UI Semibold", 12f), Bounds = new Rectangle(20, 16, 440, 26) };
            var lblInfo = new Label
            {
                Text = L.T("Beide Wege öffnen GitHub in deinem Browser. Dort füllst du alles selbst aus (GitHub-Konto nötig)."),
                Bounds = new Rectangle(20, 46, 440, 40), ForeColor = Color.DimGray, UseMnemonic = false
            };

            var btnIssue = new Button { Text = L.T("Issue erstellen"), Bounds = new Rectangle(20, 96, 210, 40) };
            var lblIssue = new Label { Text = L.T("Fehler melden oder eine Idee vorschlagen"), Bounds = new Rectangle(20, 140, 210, 40), ForeColor = Color.DimGray };
            var btnPr = new Button { Text = L.T("Pull Request erstellen"), Bounds = new Rectangle(250, 96, 210, 40) };
            var lblPr = new Label { Text = L.T("Eine Code-Änderung vorschlagen"), Bounds = new Rectangle(250, 140, 210, 40), ForeColor = Color.DimGray };
            var btnClose = new Button { Text = L.T("Abbrechen"), Bounds = new Rectangle(360, 200, 100, 32) };

            btnIssue.Click += delegate { UpdateUi.Open("https://github.com/" + AppInfo.GitHubRepo + "/issues/new"); Close(); };
            btnPr.Click += delegate { UpdateUi.Open("https://github.com/" + AppInfo.GitHubRepo + "/compare"); Close(); };
            btnClose.Click += delegate { Close(); };
            CancelButton = btnClose;
            Controls.AddRange(new Control[] { lblHead, lblInfo, btnIssue, lblIssue, btnPr, lblPr, btnClose });
            HandleCreated += delegate { Theme.Apply(this, dark); };
        }
    }
}
