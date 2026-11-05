// Akku-Anzeige Setup: installiert/aktualisiert/deinstalliert die App, erkennt Hersteller-Software.
// Laeuft mit Administratorrechten (setup.manifest). Startet die App danach als normaler Benutzer.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AkkuAnzeige
{
    static class SetupProgram
    {
        // Sprache: Windows-Sprache, ueberschreibbar mit /lang=de bzw. /lang=en
        public static bool De = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        public static string S(string de, string en) { return De ? de : en; }

        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AkkuAnzeige"); } }
        public static string AppExe { get { return Path.Combine(InstallDir, "AkkuAnzeige.exe"); } }
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AkkuAnzeige";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // z.B. "1.2.0 Alpha" (aus AssemblyInformationalVersion "1.2.0-alpha")
        public static string Version
        {
            get
            {
                var a = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                                .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
                if (a != null && a.InformationalVersion.Length > 0)
                {
                    var parts = a.InformationalVersion.Split('-');
                    return parts[0] + (parts.Length > 1 ? " " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parts[1]) : "");
                }
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Any(a => a.Equals("/lang=en", StringComparison.OrdinalIgnoreCase))) De = false;
            if (args.Any(a => a.Equals("/lang=de", StringComparison.OrdinalIgnoreCase))) De = true;
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) { Uninstall(args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase))); return; }
            if (args.Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase))) { SilentUpdate(); return; }
            Application.Run(new SetupForm());
        }

        // ---------------- Hilfen ----------------
        public static void StopApp()
        {
            foreach (var p in Process.GetProcessesByName("AkkuAnzeige"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        static byte[] Res(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("Ressource fehlt: " + name);
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        public static void Shortcut(string lnk, string target, string args, string icon)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            var st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args ?? "" });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icon + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Akku-Anzeige.lnk"); } }
        static string StartMenuLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Akku-Anzeige.lnk"); } }

        static void CertStores(Action<X509Store> a)
        {
            foreach (var n in new[] { StoreName.Root, StoreName.TrustedPublisher })
            {
                var st = new X509Store(n, StoreLocation.LocalMachine);
                st.Open(OpenFlags.ReadWrite);
                try { a(st); } finally { st.Close(); }
            }
        }

        // ---------------- Installation ----------------
        public static void Install(bool desktop, bool startMenu, bool autostart, Action<string> log)
        {
            log(S("Beende laufende Akku-Anzeige …", "Stopping running Battery Display …"));
            StopApp();

            log(S("Trage Zertifikat der Akku-Anzeige als vertrauenswürdig ein …", "Trusting the Battery Display certificate …"));
            var cert = new X509Certificate2(Res("AkkuAnzeige.cer"));
            CertStores(st => { if (st.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count == 0) st.Add(cert); });

            log(S("Kopiere Dateien nach ", "Copying files to ") + InstallDir + " …");
            Directory.CreateDirectory(InstallDir);
            File.WriteAllBytes(AppExe, Res("AkkuAnzeige.exe"));
            File.WriteAllBytes(Path.Combine(InstallDir, "AkkuAnzeige.ico"), Res("AkkuAnzeige.ico"));
            File.WriteAllBytes(Path.Combine(InstallDir, "AkkuAnzeige-Desktop.ico"), Res("AkkuAnzeige-Desktop.ico"));
            string self = Assembly.GetExecutingAssembly().Location;
            string setupCopy = Path.Combine(InstallDir, "Setup.exe");
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(setupCopy), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, setupCopy, true);

            string icon = Path.Combine(InstallDir, "AkkuAnzeige-Desktop.ico");
            if (desktop) { log(S("Desktop-Verknüpfung …", "Desktop shortcut …")); Shortcut(DesktopLnk, AppExe, "", icon); }
            else if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk);
            if (startMenu) { log(S("Startmenü-Eintrag …", "Start menu entry …")); Shortcut(StartMenuLnk, AppExe, "", icon); }
            else if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk);

            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (autostart) k.SetValue("AkkuAnzeige", "\"" + AppExe + "\" /autostart");
                else k.DeleteValue("AkkuAnzeige", false);
            }

            log(S("Eintrag unter \"Apps & Features\" …", "Entry in \"Apps & features\" …"));
            using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Akku-Anzeige");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Diabeetus");
                k.SetValue("DisplayIcon", Path.Combine(InstallDir, "AkkuAnzeige.ico"));
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + setupCopy + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + setupCopy + "\" /uninstall /quiet");
                k.SetValue("Comments", "Powered by Claude.ai");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = new DirectoryInfo(InstallDir).GetFiles().Sum(f => f.Length) / 1024;
                k.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
            }
            log(S("Fertig.", "Done."));
        }

        // App als normaler Benutzer starten (nicht mit den Adminrechten des Setups)
        public static void StartAppAsUser()
        {
            try { Process.Start("explorer.exe", "\"" + AppExe + "\""); } catch { }
        }

        public static bool IsInstalled { get { return File.Exists(AppExe); } }

        // ---------------- Stilles Update (aus der App heraus) ----------------
        // Uebernimmt die bisherigen Optionen (Verknuepfungen, Autostart, verknuepfte Treiber-Software)
        static void SilentUpdate()
        {
            bool desktop = File.Exists(DesktopLnk) || !IsInstalled;
            bool startMenu = File.Exists(StartMenuLnk) || !IsInstalled;
            bool autostart = true;
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) autostart = !IsInstalled || (k != null && k.GetValue("AkkuAnzeige") != null); } catch { }

            var f = new Form
            {
                Text = "Akku-Anzeige", FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(380, 70), TopMost = true, ShowInTaskbar = false, ControlBox = false, BackColor = Color.FromArgb(32, 32, 32)
            };
            var lbl = new Label { Text = S("Akku-Anzeige wird auf Version {0} aktualisiert …", "Updating Battery Display to version {0} …").Replace("{0}", Version),
                                  Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 10f) };
            f.Controls.Add(lbl);
            f.Show();
            Application.DoEvents();
            var log = new System.Text.StringBuilder();
            try
            {
                Install(desktop, startMenu, autostart, s => { log.AppendLine(s); Application.DoEvents(); });
                f.Close();
                StartAppAsUser();
            }
            catch (Exception ex)
            {
                f.Close();
                MessageBox.Show(S("Das Update ist fehlgeschlagen:", "The update failed:") + "\n\n" + ex.Message + "\n\n" + log, "Akku-Anzeige",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------- Deinstallation ----------------
        static void Uninstall(bool quiet)
        {
            if (!quiet && MessageBox.Show(S("Akku-Anzeige wirklich deinstallieren?", "Really uninstall Battery Display?"), "Akku-Anzeige",
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool keepSettings = quiet || MessageBox.Show(S("Einstellungen (Geräte, Monitore, Grenzwerte) behalten?", "Keep settings (devices, monitors, thresholds)?"),
                                          "Akku-Anzeige", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            StopApp();
            try { if (File.Exists(DesktopLnk)) File.Delete(DesktopLnk); } catch { }
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); } catch { }
            try { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue("AkkuAnzeige", false); } catch { }
            try { RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).DeleteSubKeyTree(UninstallKey, false); } catch { }
            try
            {
                var cert = new X509Certificate2(Res("AkkuAnzeige.cer"));
                CertStores(st => { foreach (var c in st.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)) st.Remove(c); });
            }
            catch { }
            if (!keepSettings)
            {
                try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AkkuAnzeige"), true); } catch { }
            }
            // eigene Dateien nach dem Beenden loeschen (Setup.exe laeuft gerade selbst)
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + InstallDir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            }
            catch { }
            if (!quiet) MessageBox.Show(S("Akku-Anzeige wurde deinstalliert.", "Battery Display has been uninstalled."), "Akku-Anzeige", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    class SetupForm : Form
    {
        CheckBox chkDesktop, chkStart, chkAuto, chkRun;
        ListView lvVend;
        Button btnInstall, btnCancel, btnUninstall;
        TextBox txtLog;
        static string S(string de, string en) { return SetupProgram.S(de, en); }

        public SetupForm()
        {
            bool inst = SetupProgram.IsInstalled;
            Text = "Akku-Anzeige " + SetupProgram.Version + " – Setup";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 590);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            var header = new Panel { Bounds = new Rectangle(0, 0, 620, 84), BackColor = Color.FromArgb(32, 32, 32) };
            var pic = new PictureBox { Bounds = new Rectangle(18, 14, 56, 56), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try { pic.Image = new Icon(Icon, 64, 64).ToBitmap(); } catch { }
            var lblT = new Label { Text = "Akku-Anzeige " + SetupProgram.Version, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f), Bounds = new Rectangle(88, 14, 500, 32), BackColor = Color.Transparent };
            var lblS = new Label { Text = "© 2026 Bobohatt & Diabeetus · Powered by Claude.ai", ForeColor = Color.Silver, Bounds = new Rectangle(90, 50, 500, 20), BackColor = Color.Transparent, UseMnemonic = false };
            header.Controls.AddRange(new Control[] { pic, lblT, lblS });

            var lblIntro = new Label
            {
                Text = inst ? S("Akku-Anzeige ist bereits installiert. Mit \"Aktualisieren\" wird die neue Version eingespielt – deine Einstellungen bleiben erhalten.",
                                "Battery Display is already installed. \"Update\" installs the new version – your settings are kept.")
                            : S("Zeigt die Akkustände deiner drahtlosen Geräte (Maus, Tastatur, Kopfhörer …) direkt auf der Taskleiste.",
                                "Shows the battery levels of your wireless devices (mouse, keyboard, headphones …) right on the taskbar."),
                Bounds = new Rectangle(18, 96, 584, 36)
            };

            // Nur wirklich installierte Programme; kompatible koennen verknuepft werden (Haekchen)
            var gVend = new GroupBox { Text = S("Erkannte Treiber-Software – Haken = mit Akku-Anzeige verknüpfen", "Detected driver software – checked = link with Battery Display"), Bounds = new Rectangle(14, 136, 592, 190) };
            lvVend = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Bounds = new Rectangle(10, 22, 572, 158), HeaderStyle = ColumnHeaderStyle.Nonclickable };
            lvVend.Columns.Add("Software", 175); lvVend.Columns.Add("Status", 130); lvVend.Columns.Add(S("Hinweis", "Note"), 245);
            // Eingebaute Lesewege (Bluetooth, Logitech-Empfaenger, HyperX, DualSense, AirPods) brauchen keine Software und
            // sind automatisch aktiv – sie erscheinen in der App unter "Datenquellen", sobald passende Hardware da ist.
            foreach (var v in Vendors.Detect(false))
            {
                bool ok = v.Support == VendorSupport.Supported;
                string st = (v.Running ? S("läuft", "running") : S("installiert", "installed")) + " · " + (ok ? S("kompatibel", "compatible") : S("nicht kompatibel", "not compatible"));
                var it = new ListViewItem(new[] { v.Name + (string.IsNullOrEmpty(v.Version) ? "" : " " + v.Version), st, SetupProgram.De ? v.NoteDe : v.NoteEn });
                it.Tag = ok ? v.Key : null;            // nur kompatible lassen sich verknuepfen
                it.Checked = ok && (Links.Get(v.Key) ?? true);
                if (!ok) it.ForeColor = Color.Gray;
                lvVend.Items.Add(it);
            }
            lvVend.ItemCheck += (s, e) => { if (lvVend.Items[e.Index].Tag == null) e.NewValue = CheckState.Unchecked; };
            gVend.Controls.Add(lvVend);

            var gOpt = new GroupBox { Text = S("Optionen", "Options"), Bounds = new Rectangle(14, 334, 592, 112) };
            chkDesktop = new CheckBox { Text = S("Desktop-Verknüpfung", "Desktop shortcut"), Checked = true, Bounds = new Rectangle(14, 24, 270, 24) };
            chkStart = new CheckBox { Text = S("Eintrag im Startmenü", "Start menu entry"), Checked = true, Bounds = new Rectangle(14, 50, 270, 24) };
            chkAuto = new CheckBox { Text = S("Mit Windows starten", "Start with Windows"), Checked = true, Bounds = new Rectangle(300, 24, 280, 24) };
            chkRun = new CheckBox { Text = S("Nach der Installation starten", "Start after installation"), Checked = true, Bounds = new Rectangle(300, 50, 280, 24) };
            var lblPath = new Label { Text = S("Ziel: ", "Target: ") + SetupProgram.InstallDir, Bounds = new Rectangle(16, 82, 560, 20), ForeColor = Color.DimGray };
            gOpt.Controls.AddRange(new Control[] { chkDesktop, chkStart, chkAuto, chkRun, lblPath });

            var lblCert = new Label
            {
                Text = S("Hinweis: Damit die Kacheln auch über Startmenü und Taskleisten-Menüs sichtbar bleiben, wird das eigene Zertifikat der Akku-Anzeige in Windows als vertrauenswürdig eingetragen (wird bei der Deinstallation wieder entfernt).",
                         "Note: so that the tiles stay visible above the Start menu and taskbar menus, Battery Display's own certificate is added to Windows' trusted certificates (removed again on uninstall)."),
                Bounds = new Rectangle(18, 452, 584, 48), ForeColor = Color.DimGray
            };

            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(18, 452, 584, 80), Visible = false };

            btnUninstall = new Button { Text = S("Deinstallieren", "Uninstall"), Bounds = new Rectangle(18, 544, 120, 32), Visible = inst };
            btnInstall = new Button { Text = inst ? S("Aktualisieren", "Update") : S("Installieren", "Install"), Bounds = new Rectangle(380, 544, 110, 32) };
            btnCancel = new Button { Text = S("Abbrechen", "Cancel"), Bounds = new Rectangle(496, 544, 110, 32) };
            btnInstall.Click += delegate { DoInstall(lblCert); };
            btnCancel.Click += delegate { Close(); };
            btnUninstall.Click += delegate
            {
                Hide();
                Process.Start(Assembly.GetExecutingAssembly().Location, "/uninstall").WaitForExit();
                Close();
            };
            AcceptButton = btnInstall;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { header, lblIntro, gVend, gOpt, lblCert, txtLog, btnUninstall, btnInstall, btnCancel });

            // bestehende Installation: aktuelle Wahl uebernehmen
            if (inst)
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(SetupProgram.RunKey)) chkAuto.Checked = k != null && k.GetValue("AkkuAnzeige") != null; } catch { }
            }
        }

        void Log(string s) { txtLog.AppendText(s + "\r\n"); Application.DoEvents(); }

        void DoInstall(Label lblCert)
        {
            btnInstall.Enabled = false; btnCancel.Enabled = false; btnUninstall.Enabled = false;
            lblCert.Visible = false; txtLog.Visible = true;
            try
            {
                SetupProgram.Install(chkDesktop.Checked, chkStart.Checked, chkAuto.Checked, Log);
                // Verknuepfungen der kompatiblen Treiber-Software speichern
                foreach (ListViewItem it in lvVend.Items)
                {
                    var key = it.Tag as string;
                    if (key == null) continue;
                    Links.Set(key, it.Checked);
                    Log((it.Checked ? S("Verknüpft: ", "Linked: ") : S("Nicht verknüpft: ", "Not linked: ")) + it.Text);
                }
                if (chkRun.Checked) { Log(S("Starte Akku-Anzeige …", "Starting Battery Display …")); SetupProgram.StartAppAsUser(); }
                btnCancel.Text = S("Fertig", "Finish");
                btnCancel.Enabled = true;
                AcceptButton = btnCancel;
            }
            catch (Exception ex)
            {
                Log(S("FEHLER: ", "ERROR: ") + ex.Message);
                btnCancel.Text = S("Schließen", "Close");
                btnCancel.Enabled = true;
            }
        }
    }
}
