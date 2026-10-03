using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WhalePet
{
    internal sealed class WhaleMenuRenderer : ToolStripRenderer
    {
        private static readonly Color Paper = Color.FromArgb(246, 250, 255);
        private static readonly Color Ink = Color.FromArgb(58, 78, 116);
        private static readonly Color Blue = Color.FromArgb(89, 148, 215);

        public static void Style(ToolStripDropDown menu)
        {
            menu.Renderer = new WhaleMenuRenderer();
            menu.BackColor = Paper;
            menu.ForeColor = Ink;
            menu.Font = new Font("Microsoft YaHei UI", 10f);
            menu.Padding = new Padding(7);
            menu.DropShadowEnabled = false;
            foreach (ToolStripItem entry in menu.Items)
            {
                if (entry is ToolStripSeparator) { entry.Margin = new Padding(8, 4, 8, 4); continue; }
                entry.Padding = new Padding(8, 7, 14, 7);
                entry.Margin = new Padding(0, 1, 0, 1);
                ToolStripMenuItem item = entry as ToolStripMenuItem;
                if (item != null && item.HasDropDownItems) Style(item.DropDown);
            }
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Paper);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen border = new Pen(Color.FromArgb(207, 224, 245)))
                e.Graphics.DrawRectangle(border, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            bool header = (string)e.Item.Tag == "header";
            if (!header && !e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath shape = Round(new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2), 7))
            using (Brush fill = new SolidBrush(header ? Color.FromArgb(231, 240, 255) : Color.FromArgb(221, 237, 255)))
                e.Graphics.FillPath(fill, shape);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if ((string)e.Item.Tag == "header") e.TextColor = Blue;
            else e.TextColor = e.Item.ForeColor;

            // Vertically center text to the exact middle of the entire menu item height
            Rectangle textRect = new Rectangle(
                e.TextRectangle.X,
                0,
                e.TextRectangle.Width,
                e.Item.Height);

            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textRect, e.TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = e.ImageRectangle.X + e.ImageRectangle.Width / 2;
            int y = e.Item.Height / 2;
            using (Brush fill = new SolidBrush(Blue)) e.Graphics.FillEllipse(fill, x - 8, y - 8, 16, 16);
            using (Pen tick = new Pen(Color.White, 2f))
            {
                tick.StartCap = tick.EndCap = LineCap.Round;
                e.Graphics.DrawLines(tick, new Point[] { new Point(x - 4, y), new Point(x - 1, y + 3), new Point(x + 4, y - 3) });
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int x = e.ArrowRectangle.Left + e.ArrowRectangle.Width / 2;
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(Blue, 1.7f))
                e.Graphics.DrawLines(pen, new Point[] { new Point(x - 2, y - 4), new Point(x + 2, y), new Point(x - 2, y + 4) });
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(220, 231, 245)))
                e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
        }
    }
}
