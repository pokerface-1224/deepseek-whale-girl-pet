using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WhalePet
{
    /// <summary>Hosts the DSH web app inside a clean anime-styled sticky card beside the pet.</summary>
    internal sealed class MiniPanel : Form
    {
        // Preserved fields for compatibility with MiniPanelCheck reflection
        private readonly WebView2 browser;
        private readonly Label status;

        private readonly Uri endpoint;
        private Form petAnchor;
        private bool initialized;
        private bool initializing;

        // Visual Palette matching WhaleMenuRenderer & Whale-girl
        private static readonly Color Paper = Color.FromArgb(246, 250, 255);
        private static readonly Color HeaderBg = Color.FromArgb(238, 245, 254);
        private static readonly Color Ink = Color.FromArgb(58, 78, 116);
        private static readonly Color SubText = Color.FromArgb(120, 142, 176);
        private static readonly Color Blue = Color.FromArgb(89, 148, 215);
        private static readonly Color SkyBlue = Color.FromArgb(56, 189, 248);
        private static readonly Color BorderColor = Color.FromArgb(207, 224, 245);

        private readonly AnimeTitleBar titleBar;

        internal MiniPanel(string url) : this(url, null)
        {
        }

        internal MiniPanel(string url, Form anchor)
        {
            petAnchor = anchor;
            endpoint = new Uri(url);
            if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1")
                throw new ArgumentException("Expected local DSH endpoint");

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Text = "鲸鱼娘 · 随身终端";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(420, 580);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Paper;
            Font = new Font("Microsoft YaHei UI", 9f);

            // Container for smooth margins and rounded layout
            titleBar = new AnimeTitleBar("鲸鱼娘 · 随身终端");
            titleBar.OnReloadClicked += async delegate { if (initialized) browser.Reload(); else await InitializeBrowser(); };
            titleBar.OnOpenHarnessClicked += delegate { try { HarnessWindow.Open(); } catch { SetStatusText("无法唤起 DSH 窗口", true); } };
            titleBar.OnCloseClicked += delegate { Hide(); };

            status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Text = "🐳 正在连接当前会话…",
                AutoEllipsis = true,
                BackColor = HeaderBg,
                ForeColor = SubText,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                TextAlign = ContentAlignment.MiddleCenter
            };

            Panel contentWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(3, 0, 3, 2),
                BackColor = Paper
            };

            browser = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Paper
            };
            contentWrapper.Controls.Add(browser);

            Controls.Add(contentWrapper);
            Controls.Add(status);
            Controls.Add(titleBar);

            Shown += async delegate
            {
                UpdateClipRegion();
                if (petAnchor != null) UpdatePosition(petAnchor);
                if (!initialized) await InitializeBrowser();
            };

            Resize += delegate { UpdateClipRegion(); };

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // Add CS_DROPSHADOW for a gentle modern shadow
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        private void UpdateClipRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = GetRoundedPath(new Rectangle(0, 0, Width, Height), 14))
            {
                Region = new Region(path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = GetRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 14))
            using (Pen borderPen = new Pen(BorderColor, 1.5f))
            {
                e.Graphics.DrawPath(borderPen, path);
            }
        }

        internal static GraphicsPath GetRoundedPath(Rectangle r, int radius)
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

        internal void SetAnchor(Form anchor)
        {
            petAnchor = anchor;
            if (Visible) UpdatePosition(anchor);
        }

        internal void UpdatePosition(Form anchor = null)
        {
            Form target = anchor ?? petAnchor;
            if (target == null || target.IsDisposed) return;
            Screen screen = Screen.FromControl(target);
            Rectangle area = screen.WorkingArea;

            int gap = 8;
            PetForm pet = target as PetForm;
            Rectangle visual = pet != null ? pet.StableAnchorBounds : target.Bounds;

            int targetX;
            // Prefer left side of pet if space allows
            if (visual.Left - area.Left >= Width + gap)
            {
                targetX = visual.Left - Width - gap;
            }
            else if (area.Right - visual.Right >= Width + gap)
            {
                // Dock on right side if left is crowded
                targetX = visual.Right + gap;
            }
            else
            {
                targetX = Math.Max(area.Left + 8, Math.Min(area.Right - Width - 8, visual.Left - Width - gap));
            }

            int targetY = visual.Bottom - Height;
            targetY = Math.Max(area.Top + 8, Math.Min(area.Bottom - Height - 8, targetY));
            if (Location.X != targetX || Location.Y != targetY)
            {
                Location = new Point(targetX, targetY);
            }
        }

        internal void Reveal(Form anchor = null)
        {
            if (IsDisposed) return;
            if (anchor != null) SetAnchor(anchor);
            UpdatePosition(anchor);
            if (!Visible)
            {
                try
                {
                    if (anchor != null && !anchor.IsDisposed) Show(anchor);
                    else Show();
                }
                catch
                {
                    Show();
                }
            }
            else
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                BringToFront();
            }
            Activate();
        }

        private void SetStatusText(string text, bool visible)
        {
            if (IsDisposed || status == null) return;
            status.Text = text;
            status.Visible = visible;
        }

        private async System.Threading.Tasks.Task InitializeBrowser()
        {
            if (initializing || IsDisposed) return;
            initializing = true;
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSHWhalePet", "WebView2");
                try { EnsureWritable(data); }
                catch (UnauthorizedAccessException) { data = LocalFallback(); }
                catch (IOException) { data = LocalFallback(); }
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                if (IsDisposed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (IsDisposed) return;
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    Uri target;
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out target) || target.GetLeftPart(UriPartial.Authority) != endpoint.GetLeftPart(UriPartial.Authority))
                        e.Cancel = true;
                };
                browser.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess)
                    {
                        SetStatusText("", false);
                        titleBar.SetConnected(true);
                    }
                    else
                    {
                        SetStatusText("🐳 连接中断，请确认 DSH 正在运行后重试", true);
                        titleBar.SetConnected(false);
                    }
                };
                initialized = true;
                await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(function() { try { document.documentElement.setAttribute('data-mini-panel', 'true'); } catch(e) {} })();");
                string initialSession = GetInitialSession(endpoint);
                if (!string.IsNullOrEmpty(initialSession))
                {
                    string script = string.Format(
                        "(function() {{ try {{ var k = 'dsh.sessions.current'; var s = localStorage.getItem(k); var o = s ? JSON.parse(s) : {{}}; o.sessionId = '{0}'; localStorage.setItem(k, JSON.stringify(o)); }} catch(e) {{}} }})();",
                        initialSession.Replace("'", "\\'"));
                    await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                }
                browser.CoreWebView2.Navigate(endpoint.AbsoluteUri);
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                titleBar.SetConnected(false);
                string msg = error is UnauthorizedAccessException || error is IOException
                    ? "面板数据目录不可写，请检查插件目录权限"
                    : "面板初始化失败（" + error.GetType().Name + " / " + error.HResult.ToString("X8") + "），请检查 WebView2 Runtime";
                SetStatusText(msg, true);
            }
            finally { initializing = false; }
        }

        private static void EnsureWritable(string path)
        {
            Directory.CreateDirectory(path);
            string probe = Path.Combine(path, Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }

        private static string LocalFallback()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".webview2");
            EnsureWritable(path);
            return path;
        }

        private static string GetInitialSession(Uri uri)
        {
            if (uri == null || string.IsNullOrEmpty(uri.Query)) return null;
            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    string key = part.Substring(0, eq);
                    if (string.Equals(key, "initialSession", StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(part.Substring(eq + 1));
                    }
                }
            }
            return null;
        }

        internal void SyncSession(string sessionId)
        {
            if (!initialized || IsDisposed || browser == null || browser.CoreWebView2 == null || string.IsNullOrEmpty(sessionId)) return;
            string js = string.Format(
                "try {{ window.postMessage({{ type: 'pet-whale-sync', sessionId: '{0}' }}, '*'); }} catch(e) {{}}",
                sessionId.Replace("'", "\\'"));
            browser.CoreWebView2.ExecuteScriptAsync(js);
        }
    }

    /// <summary>Anime-styled header bar with custom logo, title and pill buttons.</summary>
    internal sealed class AnimeTitleBar : Control
    {
        private static readonly Color HeaderBg = Color.FromArgb(238, 245, 254);
        private static readonly Color Ink = Color.FromArgb(58, 78, 116);
        private static readonly Color Blue = Color.FromArgb(89, 148, 215);
        private static readonly Color SkyBlue = Color.FromArgb(56, 189, 248);
        private static readonly Color BorderColor = Color.FromArgb(207, 224, 245);
        private static readonly Color IndicatorGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color IndicatorGray = Color.FromArgb(156, 163, 175);

        private readonly string titleText;
        private bool isConnected = false;

        private readonly AnimePillButton btnReload;
        private readonly AnimePillButton btnOpen;
        private readonly AnimePillButton btnClose;

        public event EventHandler OnReloadClicked;
        public event EventHandler OnOpenHarnessClicked;
        public event EventHandler OnCloseClicked;

        public AnimeTitleBar(string title)
        {
            titleText = title;
            BackColor = HeaderBg;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            btnReload = new AnimePillButton { Text = "刷新", Size = new Size(50, 26) };
            btnOpen = new AnimePillButton { Text = "主界面", Size = new Size(58, 26) };
            btnClose = new AnimePillButton { Text = "✕", Size = new Size(30, 26), IsDanger = true };

            btnReload.Click += delegate { if (OnReloadClicked != null) OnReloadClicked(this, EventArgs.Empty); };
            btnOpen.Click += delegate { if (OnOpenHarnessClicked != null) OnOpenHarnessClicked(this, EventArgs.Empty); };
            btnClose.Click += delegate { if (OnCloseClicked != null) OnCloseClicked(this, EventArgs.Empty); };

            Controls.Add(btnReload);
            Controls.Add(btnOpen);
            Controls.Add(btnClose);

            Dock = DockStyle.Top;
            Height = 44;
            LayoutButtons();
        }

        public void SetConnected(bool connected)
        {
            isConnected = connected;
            Invalidate();
        }

        private void LayoutButtons()
        {
            if (btnClose == null || btnOpen == null || btnReload == null) return;
            int right = Width - 8;
            btnClose.Location = new Point(right - btnClose.Width, (Height - btnClose.Height) / 2);
            right -= btnClose.Width + 5;
            btnOpen.Location = new Point(right - btnOpen.Width, (Height - btnOpen.Height) / 2);
            right -= btnOpen.Width + 5;
            btnReload.Location = new Point(right - btnReload.Width, (Height - btnReload.Height) / 2);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw header background
            using (Brush bgBrush = new SolidBrush(HeaderBg))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            // Draw cute vector whale logo on the left
            int logoX = 12;
            int logoY = (Height - 20) / 2;
            DrawCuteWhaleLogo(g, logoX, logoY);

            // Draw Title Text
            using (Font titleFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(Ink))
            {
                g.DrawString(titleText, titleFont, textBrush, logoX + 24, (Height - 17) / 2);
            }

            // Draw small status dot
            int dotX = logoX + 162;
            int dotY = (Height - 7) / 2;
            using (Brush dotBrush = new SolidBrush(isConnected ? IndicatorGreen : IndicatorGray))
            {
                g.FillEllipse(dotBrush, dotX, dotY, 7, 7);
            }

            // Bottom border line for header
            using (Pen linePen = new Pen(BorderColor, 1f))
            {
                g.DrawLine(linePen, 0, Height - 1, Width, Height - 1);
            }
        }

        private void DrawCuteWhaleLogo(Graphics g, int x, int y)
        {
            // Tiny blowhole fountain
            using (Pen fountainPen = new Pen(SkyBlue, 1.4f))
            {
                fountainPen.StartCap = fountainPen.EndCap = LineCap.Round;
                g.DrawLine(fountainPen, x + 9, y + 2, x + 7, y);
                g.DrawLine(fountainPen, x + 10, y + 2, x + 12, y);
            }

            // Whale body
            using (GraphicsPath whale = new GraphicsPath())
            {
                whale.AddArc(x + 2, y + 4, 15, 12, 140, 200);
                whale.AddLine(x + 18, y + 10, x + 15, y + 14);
                whale.AddLine(x + 15, y + 14, x + 3, y + 13);
                whale.CloseFigure();
                using (Brush whaleBrush = new SolidBrush(Blue))
                {
                    g.FillPath(whaleBrush, whale);
                }
            }

            // Tiny white belly curve
            using (GraphicsPath belly = new GraphicsPath())
            {
                belly.AddArc(x + 3, y + 8, 10, 6, 0, 180);
                belly.CloseFigure();
                using (Brush whiteBrush = new SolidBrush(Color.White))
                {
                    g.FillPath(whiteBrush, belly);
                }
            }

            // Tiny black eye
            using (Brush eyeBrush = new SolidBrush(Color.FromArgb(24, 38, 64)))
            {
                g.FillEllipse(eyeBrush, x + 6, y + 7, 2, 2);
            }
        }
    }

    /// <summary>Clean flat pill button with anime pastel colors and soft hover feedback.</summary>
    internal sealed class AnimePillButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;
        public bool IsDanger { get; set; }

        private static readonly Color Ink = Color.FromArgb(58, 78, 116);
        private static readonly Color NormalBg = Color.FromArgb(246, 250, 255);
        private static readonly Color HoverBg = Color.FromArgb(226, 239, 255);
        private static readonly Color PressedBg = Color.FromArgb(212, 230, 255);
        private static readonly Pen NormalBorder = new Pen(Color.FromArgb(207, 224, 245), 1.2f);
        private static readonly Pen HoverBorder = new Pen(Color.FromArgb(147, 197, 253), 1.2f);

        private static readonly Color DangerText = Color.FromArgb(183, 94, 120);
        private static readonly Color DangerHoverBg = Color.FromArgb(254, 242, 244);
        private static readonly Color DangerPressedBg = Color.FromArgb(253, 230, 235);
        private static readonly Pen DangerHoverBorder = new Pen(Color.FromArgb(244, 114, 182), 1.2f);

        public AnimePillButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = MiniPanel.GetRoundedPath(rect, 6))
            {
                Color bg;
                Pen borderPen;
                Color textColor;

                if (IsDanger)
                {
                    textColor = DangerText;
                    if (isPressed) { bg = DangerPressedBg; borderPen = DangerHoverBorder; }
                    else if (isHovered) { bg = DangerHoverBg; borderPen = DangerHoverBorder; }
                    else { bg = NormalBg; borderPen = NormalBorder; }
                }
                else
                {
                    textColor = isHovered ? Color.FromArgb(37, 99, 235) : Ink;
                    if (isPressed) { bg = PressedBg; borderPen = HoverBorder; }
                    else if (isHovered) { bg = HoverBg; borderPen = HoverBorder; }
                    else { bg = NormalBg; borderPen = NormalBorder; }
                }

                using (Brush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
                g.DrawPath(borderPen, path);

                // Draw Text
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                Rectangle textRect = isPressed ? new Rectangle(0, 1, Width, Height) : ClientRectangle;
                TextRenderer.DrawText(g, Text, Font, textRect, textColor, flags);
            }
        }
    }
}
