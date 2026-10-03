// 鲸鱼娘 · Windows 桌面宠物
//
// 原生 WinForms 实现，不依赖 Electron/Chromium：这台机器上的 Electron 构建
// 一进入 GUI 初始化就崩溃，所以改用 .NET Framework 自带的 WinForms。它天生支持
// 透明、置顶、不占任务栏，且不需要任何额外下载。
//
// 编译：tools\build-desktop-pet.ps1
// 运行：desktop-pet\启动桌宠.cmd   （或 鲸鱼娘桌宠.exe）

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WhalePet
{
    internal enum PetState { Normal, Thinking, Daydreaming, Slacking, Bored, Playing }
    /// <summary>One pose of the character sheet, with where its eyes are.</summary>
    internal sealed class Pose
    {
        public string Name;
        public Bitmap Image;
        public Rectangle VisibleBounds;
        public float EyeY;        // fraction of image height, eye centre line
        public float EyeHeight;   // fraction of image height
        public float EyeWidth;    // fraction of image width, per eye
        public float LeftEyeX;    // fraction of image width, left iris left edge
        public float RightEyeX;

        public Pose(string name, string path, float eyeY, float eyeHeight, float eyeWidth, float leftEyeX, float rightEyeX)
        {
            Name = name;
            Image = LoadUnlocked(path);
            int left = Image.Width, top = Image.Height, right = -1, bottom = -1;
            // Match hit testing: ignore nearly invisible antialiasing pixels.
            for (int y = 0; y < Image.Height; y++)
            for (int x = 0; x < Image.Width; x++)
            {
                if (Image.GetPixel(x, y).A <= 24) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            VisibleBounds = right < left ? new Rectangle(0, 0, Image.Width, Image.Height)
                : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            EyeY = eyeY;
            EyeHeight = eyeHeight;
            EyeWidth = eyeWidth;
            LeftEyeX = leftEyeX;
            RightEyeX = rightEyeX;
        }

        /// <summary>Read a PNG without keeping a file lock, so the app can be replaced while running.</summary>
        private static Bitmap LoadUnlocked(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // Fully qualified: the Pose.Image property shadows the type name.
                using (System.Drawing.Image image = System.Drawing.Image.FromStream(stream))
                {
                    Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
                    }
                    return copy;
                }
            }
        }
    }
    /// <summary>Persisted preferences, kept next to the executable.</summary>
    internal sealed class Prefs
    {
        public string Pose = "front";
        public int Size = 176;
        public bool Speech = true;
        public bool Shadow = true;
        public bool TopMost = true;
        public bool ClickThrough = true;
        public int X = int.MinValue;
        public int Y = int.MinValue;

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet-prefs.txt"); }
        }

        public static Prefs Load()
        {
            Prefs prefs = new Prefs();
            try
            {
                if (!File.Exists(FilePath)) return prefs;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    switch (key)
                    {
                        case "pose": prefs.Pose = value; break;
                        case "size": prefs.Size = ParseInt(value, prefs.Size); break;
                        case "speech": prefs.Speech = value == "1"; break;
                        case "shadow": prefs.Shadow = value == "1"; break;
                        case "topmost": prefs.TopMost = value == "1"; break;
                        case "clickthrough": prefs.ClickThrough = value == "1"; break;
                        case "x": prefs.X = ParseInt(value, prefs.X); break;
                        case "y": prefs.Y = ParseInt(value, prefs.Y); break;
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[pet] cannot read preferences: " + error.Message);
            }
            return prefs;
        }

        public void Save()
        {
            try
            {
                string[] lines = new string[]
                {
                    "pose=" + Pose,
                    "size=" + Size.ToString(CultureInfo.InvariantCulture),
                    "speech=" + (Speech ? "1" : "0"),
                    "shadow=" + (Shadow ? "1" : "0"),
                    "topmost=" + (TopMost ? "1" : "0"),
                    "clickthrough=" + (ClickThrough ? "1" : "0"),
                    "x=" + X.ToString(CultureInfo.InvariantCulture),
                    "y=" + Y.ToString(CultureInfo.InvariantCulture)
                };
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[pet] cannot save preferences: " + error.Message);
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }
    }

    /// <summary>The pet: a frameless, per-pixel transparent, always-on-top window.</summary>
    internal sealed class PetForm : Form
    {
        private readonly Dictionary<string, Pose> poses = new Dictionary<string, Pose>(StringComparer.Ordinal);
        private readonly Timer animation;
        private readonly Timer idle;
        private readonly Prefs prefs;
        private readonly string artDirectory;

        private Pose current;
        private string bubble;
        private DateTime bubbleUntil = DateTime.MinValue;
        private DateTime lastActive = DateTime.Now;
        private PetState activity = PetState.Normal;
        private DateTime stateSince = DateTime.Now;
        private int nextActivity;
        private Bitmap frame;
        private MiniPanel miniPanel;
        internal bool HasHostPipe;

        private bool dragging;
        private Point dragCursorStart;
        private Point dragWindowStart;
        private int dragDistance;

        private const int IdleAfterSeconds = 12;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        public PetForm()
        {
            artDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "art");
            prefs = Prefs.Load();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = prefs.TopMost;
            Text = "鲸鱼娘";
            ClientSize = new Size(240, 240);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            LoadPoses();
            current = poses.ContainsKey(prefs.Pose) ? poses[prefs.Pose] : poses["front"];
            ApplySize();
            RestorePosition();

            animation = new Timer { Interval = 33 };
            animation.Tick += delegate { RenderFrame(); };
            animation.Start();

            idle = new Timer { Interval = 1000 };
            idle.Tick += delegate
            {
                AdvanceActivity(DateTime.Now);
            };
            idle.Start();

            MouseDown += OnPetMouseDown;
            MouseMove += OnPetMouseMove;
            MouseUp += OnPetMouseUp;
            MouseDoubleClick += OnPetDoubleClick;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem header = new ToolStripMenuItem("鲸鱼娘  ·  桌面小憩") { Enabled = false, Tag = "header" };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem activities = new ToolStripMenuItem("状态");
            foreach (PetState value in Enum.GetValues(typeof(PetState)))
            {
                PetState selected = value;
                ToolStripMenuItem item = new ToolStripMenuItem(StateLabel(value));
                item.Click += delegate { SetActivity(selected); };
                activities.DropDownItems.Add(item);
            }
            menu.Items.Add(activities);

            ToolStripMenuItem view = new ToolStripMenuItem("视角");
            foreach (string name in new string[] { "front", "side", "back" })
            {
                string captured = name;
                ToolStripMenuItem item = new ToolStripMenuItem(Label(captured));
                item.Click += delegate { SetPose(captured); };
                view.DropDownItems.Add(item);
            }
            menu.Items.Add(view);

            ToolStripMenuItem size = new ToolStripMenuItem("大小");
            foreach (int value in new int[] { 132, 176, 232 })
            {
                int captured = value;
                ToolStripMenuItem item = new ToolStripMenuItem(captured.ToString(CultureInfo.InvariantCulture) + " px");
                item.Click += delegate { prefs.Size = captured; ApplySize(); SaveAndInvalidate(); };
                size.DropDownItems.Add(item);
            }
            menu.Items.Add(size);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem speech = new ToolStripMenuItem("气泡台词") { CheckOnClick = true, Checked = prefs.Speech };
            speech.CheckedChanged += delegate { prefs.Speech = speech.Checked; SaveAndInvalidate(); };
            menu.Items.Add(speech);

            ToolStripMenuItem shadow = new ToolStripMenuItem("投影") { CheckOnClick = true, Checked = prefs.Shadow };
            shadow.CheckedChanged += delegate { prefs.Shadow = shadow.Checked; SaveAndInvalidate(); };
            menu.Items.Add(shadow);

            ToolStripMenuItem topmost = new ToolStripMenuItem("总在最前") { CheckOnClick = true, Checked = prefs.TopMost };
            topmost.CheckedChanged += delegate { prefs.TopMost = topmost.Checked; TopMost = topmost.Checked; SaveAndInvalidate(); };
            menu.Items.Add(topmost);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openHarness = new ToolStripMenuItem("打开 dsh 窗口");
            openHarness.Click += delegate
            {
                try { HarnessWindow.Open(); }
                catch (Exception error) { Say("无法打开 Harness：" + error.Message, 5000); }
            };
            menu.Items.Insert(0, openHarness);
            ToolStripMenuItem mini = new ToolStripMenuItem("迷你面板");
            mini.Click += delegate
            {
                if (miniPanel != null && !miniPanel.IsDisposed) { miniPanel.Show(); miniPanel.Activate(); return; }
                if (!HasHostPipe)
                {
                    MessageBox.Show("请通过 DSH 插件启动桌宠，以连接当前会话。", "迷你面板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Console.WriteLine("pet:mini-panel");
                Console.Out.Flush();
            };
            menu.Items.Insert(1, mini);
            menu.Items.Insert(2, new ToolStripSeparator());

            ToolStripMenuItem home = new ToolStripMenuItem("回到右下角");
            home.Click += delegate { MoveToDefaultCorner(); };
            menu.Items.Add(home);

            ToolStripMenuItem hide = new ToolStripMenuItem("暂时隐藏 (可从 Harness 重新唤起)");
            hide.Click += delegate { Hide(); };
            menu.Items.Add(hide);

            ToolStripMenuItem quit = new ToolStripMenuItem("退出桌宠");
            quit.Click += delegate { Close(); };
            menu.Items.Add(quit);

            WhaleMenuRenderer.Style(menu);
            menu.Opening += delegate
            {
                for (int i = 0; i < activities.DropDownItems.Count; i++)
                    ((ToolStripMenuItem)activities.DropDownItems[i]).Checked = (int)activity == i;
                string[] views = { "front", "side", "back" };
                for (int i = 0; i < views.Length; i++)
                    ((ToolStripMenuItem)view.DropDownItems[i]).Checked = prefs.Pose == views[i];
                int[] sizes = { 132, 176, 232 };
                for (int i = 0; i < sizes.Length; i++)
                    ((ToolStripMenuItem)size.DropDownItems[i]).Checked = prefs.Size == sizes[i];
            };
            quit.ForeColor = Color.FromArgb(183, 94, 120);
            ContextMenuStrip = menu;
        }

        internal void ReceiveHostMessage(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            string trimmed = line.Trim();
            if (trimmed == "wake")
            {
                if (!Visible) Show();
                TopMost = prefs.TopMost;
                BringToFront();
                Activate();
                Greet();
                return;
            }
            if (line.StartsWith("panel-error:", StringComparison.Ordinal))
            {
                MessageBox.Show("DSH 会话服务尚未就绪，请稍后重试。", "迷你面板");
                return;
            }
            if (!line.StartsWith("panel:", StringComparison.Ordinal)) return;
            if (miniPanel == null || miniPanel.IsDisposed) miniPanel = new MiniPanel(line.Substring(6));
            Rectangle area = Screen.FromControl(this).WorkingArea;
            miniPanel.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - miniPanel.Width, Left - miniPanel.Width - 8)),
                Math.Max(area.Top, Math.Min(area.Bottom - miniPanel.Height, Top)));
            miniPanel.Show(this);
            miniPanel.Activate();
        }

        private static string Label(string pose)
        {
            if (pose == "front") return "正面";
            if (pose == "side") return "侧面";
            return "背面";
        }

        private void LoadPoses()
        {
            foreach (string name in new string[] { "thinking", "daydreaming", "slacking", "bored", "playing" })
                poses[name] = new Pose(name, Path.Combine(artDirectory, name + ".png"), 0, 0, 0, 0, 0);
            // Eye geometry measured off the artwork (see tools/cut_sprites.py).
            poses["front"] = new Pose("front", Path.Combine(artDirectory, "front.png"), 0.495f, 0.075f, 0.150f, 0.265f, 0.625f);
            poses["side"] = new Pose("side", Path.Combine(artDirectory, "side.png"), 0.495f, 0.075f, 0.120f, 0.300f, 0.560f);
            poses["back"] = new Pose("back", Path.Combine(artDirectory, "back.png"), 0.495f, 0.075f, 0.000f, 0.000f, 0.000f);
        }

        /// <summary>Current sprite scale: requested height over the sprite's own height.</summary>
        private float SpriteScale
        {
            get { return Math.Min(prefs.Size / (float)current.Image.Height, (ClientSize.Width - 64f) / current.Image.Width); }
        }

        private void ApplySize()
        {
            int scaledWidth = (int)Math.Round(prefs.Size * 1.4);
            ClientSize = new Size(Math.Max(240, scaledWidth + 88), prefs.Size + 100);
            if (IsHandleCreated) Location = ClampToScreen(Location);
        }

        private void RestorePosition()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            bool stored = prefs.X != int.MinValue && prefs.Y != int.MinValue;
            int x = stored ? prefs.X : area.Right - Width - 32;
            int y = stored ? prefs.Y : area.Bottom - Height - 24;
            Location = ClampToScreen(new Point(x, y));
            if (!stored) RememberPosition();
        }

        private void MoveToDefaultCorner()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = ClampToScreen(new Point(area.Right - Width - 32, area.Bottom - Height - 24));
            RememberPosition();
        }

        private Point ClampToScreen(Point point)
        {
            Rectangle visible = VisibleSpriteBounds();
            // The transparent window origin can be outside the current monitor.
            Point anchor = dragging ? Cursor.Position : new Point(point.X + visible.Left + visible.Width / 2,
                point.Y + visible.Top + visible.Height / 2);
            return ClampVisible(point, visible, Screen.FromPoint(anchor).WorkingArea);
        }

        internal static Point ClampVisible(Point point, Rectangle visible, Rectangle area)
        {
            int minX = area.Left - visible.Left, maxX = area.Right - visible.Right;
            int minY = area.Top - visible.Top, maxY = area.Bottom - visible.Bottom;
            return new Point(Math.Min(Math.Max(minX, point.X), Math.Max(minX, maxX)),
                Math.Min(Math.Max(minY, point.Y), Math.Max(minY, maxY)));
        }

        private Rectangle VisibleSpriteBounds()
        {
            float scale = SpriteScale;
            int width = (int)Math.Round(current.Image.Width * scale);
            int height = (int)Math.Round(current.Image.Height * scale);
            int left = (ClientSize.Width - width) / 2, top = ClientSize.Height - height - 20;
            Rectangle source = current.VisibleBounds;
            return Rectangle.FromLTRB(left + (int)Math.Floor(source.Left * width / (double)current.Image.Width),
                top + (int)Math.Floor(source.Top * height / (double)current.Image.Height),
                left + (int)Math.Ceiling(source.Right * width / (double)current.Image.Width),
                top + (int)Math.Ceiling(source.Bottom * height / (double)current.Image.Height));
        }

        private void RememberPosition()
        {
            prefs.X = Location.X;
            prefs.Y = Location.Y;
            prefs.Save();
        }

        private void SaveAndInvalidate()
        {
            prefs.Save();
            Invalidate();
        }

        // ------------------------------------------------------------ behaviour

        private void SetPose(string name)
        {
            if (!poses.ContainsKey(name)) return;
            prefs.Pose = name;
            current = poses[name];
            ApplySize();
            Touch();
            Say(name == "front" ? "正面登场！" : name == "side" ? "从这边看也好看～" : "看我的鲸鱼尾巴！", 2000);
            SaveAndInvalidate();
        }

        private void Say(string text, int milliseconds)
        {
            if (!prefs.Speech || string.IsNullOrEmpty(text)) return;
            bubble = text;
            bubbleUntil = DateTime.Now.AddMilliseconds(milliseconds);
            Invalidate();
        }

        /// <summary>First visible moment: a greeting bubble, so she is easy to spot.</summary>
        public void Greet()
        {
            string[] greetings = new string[] { "今天也一起写代码吧～", "我来陪你干活啦！", "桌面巡逻中～" };
            Say(greetings[new Random().Next(greetings.Length)], 3600);
        }

        private void Touch()
        {
            lastActive = DateTime.Now;
            activity = PetState.Normal;
            stateSince = lastActive;
            bubble = null;
        }

        private static string StateLabel(PetState value)
        {
            return new string[] { "普通", "思考", "发呆", "摸鱼", "无聊", "玩耍" }[(int)value];
        }

        private void SetActivity(PetState value)
        {
            Touch();
            activity = value;
            if (value != PetState.Normal) current = poses[value.ToString().ToLowerInvariant()];
            else current = poses.ContainsKey(prefs.Pose) ? poses[prefs.Pose] : poses["front"];
            ApplySize();
            RenderFrame();
        }

        private void AdvanceActivity(DateTime now)
        {
            if (dragging || (ContextMenuStrip != null && ContextMenuStrip.Visible)) return;
            if ((now - lastActive).TotalSeconds < IdleAfterSeconds) return;
            if (activity != PetState.Normal && (now - stateSince).TotalSeconds < 10) return;
            SetActivity((PetState)(1 + nextActivity++ % 5));
            // An automatic state change is not user activity.
            lastActive = now.AddSeconds(-IdleAfterSeconds);
            stateSince = now;
        }

        // -------------------------------------------------------------- pointer

        private void OnPetMouseDown(object sender, MouseEventArgs e)
        {
            SetActivity(PetState.Normal);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragDistance = 0;
            dragCursorStart = Cursor.Position;
            dragWindowStart = Location;
            Capture = true;
        }

        private void OnPetMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point now = Cursor.Position;
            int dx = now.X - dragCursorStart.X;
            int dy = now.Y - dragCursorStart.Y;
            dragDistance = Math.Max(dragDistance, Math.Abs(dx) + Math.Abs(dy));
            if (dragDistance <= 3) return;
            Location = ClampToScreen(new Point(dragWindowStart.X + dx, dragWindowStart.Y + dy));
        }

        private void OnPetMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Capture = false;
            if (dragDistance > 3)
            {
                RememberPosition();
                Say("这儿视野不错！", 1600);
                return;
            }
            Touch();
            SetActivity(PetState.Normal);
        }

        private void OnPetDoubleClick(object sender, MouseEventArgs e)
        {
            SetActivity(PetState.Normal);
        }

        /// <summary>Per-pixel hit testing: the window only reacts where she is opaque.</summary>
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_NCHITTEST && prefs != null && prefs.ClickThrough)
            {
                base.WndProc(ref message);
                if ((int)message.Result != HTTRANSPARENT)
                {
                    long packed = message.LParam.ToInt64();
                    Point client = PointToClient(new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)));
                    if (!IsOpaqueAt(client))
                    {
                        message.Result = (IntPtr)HTTRANSPARENT;
                        return;
                    }
                }
                return;
            }
            base.WndProc(ref message);
        }

        private bool IsOpaqueAt(Point client)
        {
            return frame != null && client.X >= 0 && client.Y >= 0 && client.X < frame.Width && client.Y < frame.Height
                && frame.GetPixel(client.X, client.Y).A > 24;
        }

        private int BobOffset()
        {
            if (dragging || NearScreenEdge()) return 0;
            double phase = Environment.TickCount / 1000.0;
            if (activity == PetState.Playing) return -(int)Math.Round(12 * Math.Abs(Math.Sin(phase * 2.8)));
            if (activity == PetState.Daydreaming) return (int)Math.Round(4 * Math.Sin(phase * 0.7));
            return (int)Math.Round(2 * Math.Sin(phase * 1.5));
        }

        private bool NearScreenEdge()
        {
            Rectangle visible = VisibleSpriteBounds();
            visible.Offset(Location);
            Rectangle area = Screen.FromRectangle(visible).WorkingArea;
            return visible.Left - area.Left < 16 || area.Right - visible.Right < 16
                || visible.Top - area.Top < 16 || area.Bottom - visible.Bottom < 16;
        }

        // --------------------------------------------------------------- render

        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x80000 | 0x80; return value; }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width, Height; public NativeSize(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref NativePoint position,
            ref NativeSize size, IntPtr source, ref NativePoint origin, uint key, ref Blend blend, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

        private Bitmap CreateFrame()
        {
            Bitmap image = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);
                DrawScene(graphics);
            }
            return image;
        }

        private void RenderFrame()
        {
            if (!IsHandleCreated || IsDisposed || current == null) return;
            Bitmap next = CreateFrame();
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                bitmap = next.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmap);
                NativePoint position = new NativePoint(Left, Top), origin = new NativePoint(0, 0);
                NativeSize size = new NativeSize(next.Width, next.Height);
                Blend blend = new Blend { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (frame != null) frame.Dispose();
                frame = next;
                next = null;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
                if (next != null) next.Dispose();
            }
        }

        private void DrawActivity(Graphics graphics, int left, int top, int width, int height, double phase)
        {
            if (activity == PetState.Normal) return;
            float x = left + width - 6, y = top + height * 0.40f;
            using (Pen ink = new Pen(Color.FromArgb(245, 90, 156, 223), 2.5f))
            using (SolidBrush light = new SolidBrush(Color.FromArgb(235, 220, 243, 255)))
            using (SolidBrush blue = new SolidBrush(Color.FromArgb(245, 99, 188, 235)))
            using (Font font = new Font("Microsoft YaHei", 10f, FontStyle.Bold))
            {
                // A small caption stays readable even when speech is disabled.
                SizeF label = graphics.MeasureString(StateLabel(activity), font);
                float captionX = (ClientSize.Width - label.Width) / 2;
                using (GraphicsPath pill = Rounded(new Rectangle((int)captionX - 10, 7, (int)label.Width + 20, 27), 10))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(225, 30, 51, 83)))
                    graphics.FillPath(fill, pill);
                graphics.DrawString(StateLabel(activity), font, light, captionX, 10);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Transparent window: never clear with an opaque colour.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The complete ARGB surface is submitted via UpdateLayeredWindow.
        }

        private void DrawScene(Graphics graphics)
        {
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = SpriteScale;
            int width = (int)Math.Round(current.Image.Width * scale);
            int height = (int)Math.Round(current.Image.Height * scale);
            int left = (ClientSize.Width - width) / 2;
            int bob = BobOffset();
            int top = ClientSize.Height - height - 20 + bob;

            float rotation = 0f;
            double phase = (DateTime.Now - stateSince).TotalSeconds;
            if (activity == PetState.Thinking) rotation = (float)Math.Sin(phase) * 1.2f;
            if (activity == PetState.Slacking) rotation = (float)(3 * Math.Sin(phase * 1.2));
            if (activity == PetState.Bored) rotation = 0f;
            if (activity == PetState.Playing) rotation = (float)(7 * Math.Sin(phase * 3));
            if (dragging || NearScreenEdge()) rotation = 0f;

            if (prefs.Shadow)
            {
                int shadowWidth = (int)(width * 0.58);
                int shadowHeight = 9;
                int shadowLeft = (ClientSize.Width - shadowWidth) / 2;
                int shadowTop = ClientSize.Height - shadowHeight - 12;
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(shadowLeft, shadowTop, shadowWidth, shadowHeight);
                    using (PathGradientBrush brush = new PathGradientBrush(path))
                    {
                        brush.CenterColor = Color.FromArgb(90, 8, 14, 30);
                        brush.SurroundColors = new Color[] { Color.FromArgb(0, 8, 14, 30) };
                        graphics.FillPath(brush, path);
                    }
                }
            }

            int drawTop = top;

            GraphicsState state = graphics.Save();
            if (Math.Abs(rotation) > 0.01f)
            {
                graphics.TranslateTransform(left + width / 2f, drawTop + height);
                graphics.RotateTransform(rotation);
                graphics.TranslateTransform(-(left + width / 2f), -(drawTop + height));
            }
            graphics.DrawImage(current.Image, new Rectangle(left, drawTop, width, height));
            graphics.Restore(state);

            DrawActivity(graphics, left, drawTop, width, height, phase);

            if (!string.IsNullOrEmpty(bubble) && DateTime.Now < bubbleUntil)
            {
                DrawBubble(graphics, bubble, left + width / 2, top - 6);
            }
            else if (DateTime.Now >= bubbleUntil)
            {
                bubble = null;
            }
        }

        private void DrawBubble(Graphics graphics, string text, int centerX, int bottomY)
        {
            using (Font font = new Font("Microsoft YaHei", 9f))
            {
                SizeF size = graphics.MeasureString(text, font);
                int paddingX = 9;
                int paddingY = 5;
                int bubbleWidth = (int)Math.Ceiling(size.Width) + paddingX * 2;
                int bubbleHeight = (int)Math.Ceiling(size.Height) + paddingY * 2;
                int left = Math.Max(0, Math.Min(ClientSize.Width - bubbleWidth, centerX - bubbleWidth / 2));
                int top = Math.Max(0, bottomY - bubbleHeight);

                Rectangle box = new Rectangle(left, top, bubbleWidth, bubbleHeight);
                using (GraphicsPath path = Rounded(box, 10))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 21, 26, 40)))
                using (Pen border = new Pen(Color.FromArgb(60, 255, 255, 255)))
                {
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(border, path);
                }

                // Little tail pointing at her head.
                Point[] tail = new Point[]
                {
                    new Point(centerX - 4, top + bubbleHeight - 1),
                    new Point(centerX + 4, top + bubbleHeight - 1),
                    new Point(centerX, top + bubbleHeight + 6)
                };
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 21, 26, 40)))
                {
                    graphics.FillPolygon(fill, tail);
                }

                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(255, 238, 242, 251)))
                {
                    graphics.DrawString(text, font, textBrush, left + paddingX, top + paddingY);
                }
            }
        }

        private static GraphicsPath Rounded(Rectangle box, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            RememberPosition();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animation != null) animation.Dispose();
                if (frame != null) frame.Dispose();
                if (idle != null) idle.Dispose();
                if (miniPanel != null) miniPanel.Dispose();
                foreach (Pose pose in poses.Values) if (pose.Image != null) pose.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class HarnessWindow
    {
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr handle, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);

        internal static void Open()
        {
            string executable = Environment.GetEnvironmentVariable("DSH_WHALE_HARNESS_EXE");
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName("DeepSeek Harness"))
            using (process)
            {
                try
                {
                    IntPtr window = process.MainWindowHandle;
                    if (window != IntPtr.Zero)
                    {
                        ShowWindowAsync(window, IsIconic(window) ? 9 : 5);
                        if (SetForegroundWindow(window)) return;
                    }
                    if (string.IsNullOrEmpty(executable)) executable = process.MainModule.FileName;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            // A second launch is routed to DSH's existing instance, including
            // its hidden tray window. It does not start a duplicate session.
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                throw new InvalidOperationException("请先启动 DSH，或通过 DSH 插件启动桌宠");
            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo(executable);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
            start.EnvironmentVariables.Remove("NODE_OPTIONS");
            using (System.Diagnostics.Process launched = System.Diagnostics.Process.Start(start)) { }
        }
    }

    internal static class Program
    {
        /// <summary>
        /// Optional startup log, enabled with `--log=path`. Written at every stage
        /// so a silent failure ([security software] killing the process, or the
        /// window never appearing) leaves evidence behind.
        /// </summary>
        private static string logPath;
        private static volatile bool hostClosed;

        private static void Log(string message)
        {
            if (string.IsNullOrEmpty(logPath)) return;
            try
            {
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Logging must never be the reason the pet fails.
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var instance = new System.Threading.Mutex(true, @"Local\DSH.WhalePet", out created))
            {
                // Allow an old plugin generation a moment to close its pipe.
                if (!created)
                {
                    try { if (!instance.WaitOne(1500)) return; }
                    catch (System.Threading.AbandonedMutexException) { }
                }
                try { Run(args); }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static void Run(string[] args)
        {
            bool smoke = false;
            bool hostPipe = false;
            int smokeMilliseconds = 4000;
            foreach (string arg in args)
            {
                if (arg == "--host-pipe") hostPipe = true;
                if (arg == "--smoke") smoke = true;
                if (arg.StartsWith("--smoke=", StringComparison.Ordinal))
                {
                    smoke = true;
                    int parsed;
                    if (int.TryParse(arg.Substring(8), out parsed)) smokeMilliseconds = parsed;
                }
                if (arg.StartsWith("--log=", StringComparison.Ordinal)) logPath = arg.Substring(6);
            }

            Log("start: pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " args=" + string.Join(" ", args));
            Log("base directory: " + AppDomain.CurrentDomain.BaseDirectory);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                PetForm form = new PetForm();
                form.HasHostPipe = hostPipe;
                Timer hostTimer = new Timer { Interval = 250 };
                if (hostPipe)
                {
                    var reader = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            string line;
                            while ((line = Console.In.ReadLine()) != null)
                            {
                                string message = line;
                                if (!form.IsDisposed && form.IsHandleCreated)
                                    form.BeginInvoke(new Action(delegate { if (!form.IsDisposed) form.ReceiveHostMessage(message); }));
                            }
                        }
                        catch (IOException) { }
                        catch (InvalidOperationException) { }
                        finally { hostClosed = true; }
                    });
                    reader.IsBackground = true;
                    reader.Start();
                    hostTimer.Tick += delegate { if (hostClosed) form.Close(); };
                    hostTimer.Start();
                }
                Log("form constructed: " + form.Width + "x" + form.Height);
                form.Shown += delegate
                {
                    Log("shown at " + form.Location.X + "," + form.Location.Y + " visible=" + form.Visible + " hwnd=" + form.Handle.ToInt64());
                    Console.WriteLine("[pet] shown at " + form.Location.X + "," + form.Location.Y + " size " + form.Width + "x" + form.Height);
                    // Deterministic arrival: raise her without stealing focus, and say
                    // hello, so "did it start?" is answerable at a glance.
                    form.Greet();
                    if (!smoke) return;
                    Timer quit = new Timer { Interval = smokeMilliseconds };
                    quit.Tick += delegate { quit.Stop(); form.Close(); };
                    quit.Start();
                };
                Log("entering message loop");
                Application.Run(form);
                hostTimer.Dispose();
                form.Dispose();
                Log("message loop ended");
            }
            catch (Exception error)
            {
                Log("FATAL: " + error);
                throw;
            }
        }
    }
}
