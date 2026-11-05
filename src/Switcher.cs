// Geraet direkt in der Taskleiste tauschen: kleines Fenster ueber der angeklickten Kachel
// (Design wie das Programmfenster, hell/dunkel). Ein Klick auf ein Geraet wird sofort uebernommen und gespeichert.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace AkkuAnzeige
{
    class DeviceSwitcher : Form
    {
        readonly AppCtx ctx;
        readonly string monId;
        readonly int index;
        readonly bool dark;
        readonly float s;
        readonly List<DeviceEntry> items;
        readonly List<string> current;   // Geraete-IDs der Kacheln dieses Monitors
        readonly Font fTitle, fSub, fName, fPct, fGlyph, fFoot, fFootGlyph;
        readonly int headH, rowH, footH, pad, visibleRows;
        int hover = -1, scroll;          // hover: Zeile, -2 = Fusszeile
        bool hoverFoot;
        readonly Timer outside;

        public DeviceSwitcher(AppCtx c, string monitorId, int tileIndex, Rectangle tile, bool darkMode)
        {
            ctx = c; monId = monitorId; index = tileIndex; dark = darkMode;
            s = Math.Max(1f, ctx.Scale);
            var mon = ctx.Settings.KnownMonitors.FirstOrDefault(m => m.Id == monId);
            current = mon != null && mon.Devices != null ? mon.Devices.Select(d => d.Id).ToList() : new List<string>();
            items = ctx.SwitchCandidates(monId);
            // aktuelles Geraet zuerst, dann verbundene, dann der Rest (jeweils nach Namen)
            string curId = index < current.Count ? current[index] : null;
            items = items.OrderBy(d => d.Id == curId ? 0 : ctx.Get(d.Id) != null && ctx.Get(d.Id).Connected ? 1 : 2)
                         .ThenBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase).ToList();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Back(dark);
            Text = L.T("Gerät tauschen");

            fTitle = Px("Segoe UI Semibold", 14.5f);
            fSub = Px("Segoe UI", 12f);
            fName = Px("Segoe UI", 13.5f);
            fPct = Px("Segoe UI Semibold", 13.5f);
            fFoot = Px("Segoe UI", 12.5f);
            fGlyph = new Font("Segoe Fluent Icons", 18 * s, FontStyle.Regular, GraphicsUnit.Pixel);
            if (fGlyph.Name != "Segoe Fluent Icons") { fGlyph.Dispose(); fGlyph = new Font("Segoe MDL2 Assets", 18 * s, FontStyle.Regular, GraphicsUnit.Pixel); }
            string gf = fGlyph.Name;
            fFootGlyph = new Font(gf, 13 * s, FontStyle.Regular, GraphicsUnit.Pixel);

            pad = (int)(8 * s);
            headH = (int)(58 * s);
            rowH = (int)(46 * s);
            footH = (int)(38 * s);
            var scr = Screen.FromRectangle(tile).WorkingArea;
            int maxRows = Math.Max(3, (scr.Height - headH - footH - 40) / rowH);
            visibleRows = Math.Max(1, Math.Min(Math.Min(items.Count, 9), maxRows));
            int w = (int)(360 * s);
            int h = headH + Math.Max(1, visibleRows) * rowH + footH + pad;
            if (items.Count == 0) h = headH + rowH + footH + pad;
            Size = new Size(w, h);

            // ueber der Kachel, linksbuendig; am Bildschirmrand innerhalb bleiben
            var full = Screen.FromRectangle(tile).Bounds;
            int x = Math.Max(full.Left + 4, Math.Min(tile.Left, full.Right - w - 4));
            int y = tile.Top - h - (int)(8 * s);
            if (y < full.Top) y = tile.Bottom + (int)(8 * s);
            Location = new Point(x, y);

            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Deactivate += delegate { Close(); };
            // Sicherheitsnetz, falls Windows das Fenster nicht aktiviert: Klick ausserhalb schliesst es
            outside = new Timer { Interval = 80 };
            outside.Tick += delegate
            {
                if (Control.MouseButtons != MouseButtons.None && !Bounds.Contains(Cursor.Position)) Close();
            };
            Shown += delegate { outside.Start(); };
            FormClosed += delegate { outside.Stop(); outside.Dispose(); };
        }

        Font Px(string fam, float px)
        {
            var f = new Font(fam, px * s, FontStyle.Regular, GraphicsUnit.Pixel);
            if (f.Name == fam) return f;
            f.Dispose();
            return new Font(fam.Contains("Semibold") ? "Segoe UI Semibold" : "Segoe UI", px * s, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public bool Matches(string monitorId, int tileIndex) { return monId == monitorId && index == tileIndex; }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x8;      // TOOLWINDOW | TOPMOST
                cp.ClassStyle |= 0x20000;      // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(Handle, false);
            Theme.DwmBorder(Handle, Theme.Border(dark));
        }

        Rectangle RowRect(int i) { return new Rectangle(pad, headH + (i - scroll) * rowH, Width - 2 * pad, rowH); }
        Rectangle FootRect { get { return new Rectangle(pad, Height - footH - pad / 2, Width - 2 * pad, footH); } }

        int RowAt(Point p)
        {
            if (p.Y < headH || p.Y >= headH + visibleRows * rowH || p.X < pad || p.X > Width - pad) return -1;
            int i = (p.Y - headH) / rowH + scroll;
            return i < items.Count ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = RowAt(e.Location);
            bool f = FootRect.Contains(e.Location);
            if (h != hover || f != hoverFoot) { hover = h; hoverFoot = f; Invalidate(); }
            Cursor = h >= 0 || f ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1 || hoverFoot) { hover = -1; hoverFoot = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, items.Count - visibleRows);
            int ns = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta)));
            if (ns != scroll) { scroll = ns; hover = RowAt(PointToClient(Cursor.Position)); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (FootRect.Contains(e.Location)) { Close(); ctx.ShowSettings(); return; }
            int i = RowAt(e.Location);
            if (i < 0) return;
            var chosen = items[i];
            Close();
            ctx.SwitchDevice(monId, index, chosen); // sofort uebernehmen und speichern
        }

        string StatusText(DeviceEntry d, out Color? pctColor, out string pct)
        {
            var info = ctx.Get(d.Id);
            int? p = ctx.DisplayPercent(d.Id);
            bool online = ctx.IsOnline(d.Id);
            pct = p.HasValue ? p.Value + " %" : "--";
            bool chg = online && info != null && info.Charging;
            if (!p.HasValue || (!online && ctx.GrayOffline)) pctColor = null;
            else if (chg) pctColor = ctx.ColorOf(Palette.Charging);
            else pctColor = ctx.LevelColor(p.Value);

            string st;
            if (info == null) st = d.Id.StartsWith("logi:") ? L.T("Ruhe/aus") : L.T("nicht gefunden");
            else if (!info.Connected) st = d.Id.StartsWith("logi:") ? L.T("Ruhe/aus") : L.T("getrennt");
            else if (chg) st = L.T("lädt");
            else st = L.T("verbunden");
            if (info != null && !string.IsNullOrEmpty(info.Source)) st = info.Source + " · " + st;
            int pos = current.IndexOf(d.Id);
            if (pos >= 0 && pos != index) st += " · " + L.F("↔ Kachel {0}", pos + 1);
            return st;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fg = Theme.Fore(dark), dim = Theme.Dim(dark), accent = Theme.Accent(dark);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

            // Kopf
            int tx = (int)(14 * s);
            TextRenderer.DrawText(g, L.F("Kachel {0} – Gerät wählen", index + 1), fTitle, new Rectangle(tx, (int)(10 * s), Width - 2 * tx, (int)(22 * s)), fg, flags);
            TextRenderer.DrawText(g, L.T("Ein Klick übernimmt das Gerät sofort."), fSub, new Rectangle(tx, (int)(32 * s), Width - 2 * tx, (int)(18 * s)), dim, flags);

            string curId = index < current.Count ? current[index] : null;
            if (items.Count == 0)
                TextRenderer.DrawText(g, L.T("Keine Geräte gefunden."), fName, new Rectangle(tx, headH, Width - 2 * tx, rowH), dim, flags);

            for (int i = scroll; i < items.Count && i < scroll + visibleRows; i++)
            {
                var d = items[i];
                var r = RowRect(i);
                bool isCur = d.Id == curId;
                if (isCur || i == hover)
                {
                    Color bg = isCur ? (dark ? Color.FromArgb(45, 64, 86) : Color.FromArgb(221, 236, 250)) : Theme.Hover(dark);
                    if (isCur && i == hover) bg = dark ? Color.FromArgb(52, 74, 100) : Color.FromArgb(208, 228, 247);
                    using (var path = Round(r, (int)(5 * s))) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                }
                if (isCur)
                    using (var b = new SolidBrush(accent))
                    using (var path = Round(new Rectangle(r.X + 1, r.Y + (int)(12 * s), (int)(3 * s), r.Height - (int)(24 * s)), (int)(1.5f * s)))
                        g.FillPath(b, path);

                Color? pc; string pct;
                string st = StatusText(d, out pc, out pct);
                Color iconCol = pc ?? Color.Gray;
                TextRenderer.DrawText(g, Icons.Glyph(d.Icon), fGlyph, new Rectangle(r.X + (int)(8 * s), r.Y, (int)(30 * s), r.Height), iconCol,
                                      TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int pw = (int)(52 * s);
                int nx = r.X + (int)(46 * s), nw = r.Width - (nx - r.X) - pw - (int)(8 * s);
                TextRenderer.DrawText(g, d.Label, fName, new Rectangle(nx, r.Y + (int)(4 * s), nw, (int)(21 * s)), fg, flags);
                TextRenderer.DrawText(g, st, fSub, new Rectangle(nx, r.Y + (int)(24 * s), nw, (int)(17 * s)), dim, flags);
                TextRenderer.DrawText(g, pct, fPct, new Rectangle(r.Right - pw - (int)(8 * s), r.Y, pw, r.Height), pc ?? dim,
                                      TextFormatFlags.NoPrefix | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // Bildlauf-Hinweis bei vielen Geraeten
            if (items.Count > visibleRows)
            {
                float trackH = visibleRows * rowH - 8 * s;
                float thumbH = Math.Max(16 * s, trackH * visibleRows / items.Count);
                float thumbY = headH + 4 * s + (trackH - thumbH) * scroll / Math.Max(1, items.Count - visibleRows);
                using (var b = new SolidBrush(Theme.Border(dark))) g.FillRectangle(b, Width - pad / 2f - 2 * s, thumbY, 2 * s, thumbH);
            }

            // Trennlinie + Fusszeile "Einstellungen"
            int ly = Height - footH - pad / 2 - (int)(2 * s);
            using (var pen = new Pen(Theme.Border(dark))) g.DrawLine(pen, pad, ly, Width - pad, ly);
            var fr = FootRect;
            if (hoverFoot) using (var path = Round(fr, (int)(5 * s))) using (var b = new SolidBrush(Theme.Hover(dark))) g.FillPath(b, path);
            TextRenderer.DrawText(g, "", fFootGlyph, new Rectangle(fr.X + (int)(8 * s), fr.Y, (int)(22 * s), fr.Height), dim,
                                  TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, L.T("Alle Einstellungen …"), fFoot, new Rectangle(fr.X + (int)(34 * s), fr.Y, fr.Width - (int)(40 * s), fr.Height), fg, flags);
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, 2 * rad);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var f in new[] { fTitle, fSub, fName, fPct, fGlyph, fFoot, fFootGlyph }) if (f != null) f.Dispose();
            base.Dispose(disposing);
        }
    }

    // Farben fuer das Rechtsklick-Menue (wie das Programmfenster)
    class MenuColors : ProfessionalColorTable
    {
        readonly bool d;
        public MenuColors(bool dark) { d = dark; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return Theme.Page(d); } }
        public override Color MenuBorder { get { return Theme.Border(d); } }
        public override Color MenuItemBorder { get { return Theme.Hover(d); } }
        public override Color MenuItemSelected { get { return Theme.Hover(d); } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover(d); } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover(d); } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Hover(d); } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Hover(d); } }
        public override Color ImageMarginGradientBegin { get { return Theme.Page(d); } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Page(d); } }
        public override Color ImageMarginGradientEnd { get { return Theme.Page(d); } }
        public override Color SeparatorDark { get { return Theme.Border(d); } }
        public override Color SeparatorLight { get { return Theme.Page(d); } }
    }
}
