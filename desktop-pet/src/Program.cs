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
using System.Web.Script.Serialization;

namespace WhalePet
{
    internal enum PetState { Normal, Working, Playing, Slacking, Thinking, Sleep }
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
        public int Satiety = 60;
        public int SatietyElapsedMs;

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
                        case "satiety": prefs.Satiety = Math.Max(0, Math.Min(100, ParseInt(value, 60))); break;
                        case "satietyElapsedMs": prefs.SatietyElapsedMs = Math.Max(0, Math.Min(299999, ParseInt(value, 0))); break;
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
                    "y=" + Y.ToString(CultureInfo.InvariantCulture),
                    "satiety=" + Satiety.ToString(CultureInfo.InvariantCulture),
                    "satietyElapsedMs=" + SatietyElapsedMs.ToString(CultureInfo.InvariantCulture)
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

    /// <summary>DeepSeek account info and wallet balance data.</summary>
    internal sealed class AccountData
    {
        public bool Authenticated = false;
        public string Name = "";
        public decimal NormalBalance = 0m;
        public decimal BonusBalance = 0m;
        public decimal TotalBalance = 0m;
        public string Currency = "CNY";

        public static AccountData Parse(string json)
        {
            AccountData data = new AccountData();
            if (string.IsNullOrEmpty(json)) return data;
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return data;
                object value;
                data.Authenticated = root.TryGetValue("authenticated", out value) && value is bool && (bool)value;
                var user = root.TryGetValue("user", out value) ? value as Dictionary<string, object> : null;
                data.Name = GetString(user, "name", "");
                var balance = root.TryGetValue("balance", out value) ? value as Dictionary<string, object> : null;
                data.Currency = GetString(balance, "currency", "CNY");
                data.NormalBalance = ExtractDecimal(balance, "normal");
                data.BonusBalance = ExtractDecimal(balance, "bonus");
                data.TotalBalance = ExtractDecimal(balance, "total");
            }
            catch { }
            return data;
        }

        private static string GetString(Dictionary<string, object> values, string key, string fallback)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) && value is string ? (string)value : fallback;
        }

        private static decimal ExtractDecimal(Dictionary<string, object> values, string key)
        {
            object value;
            decimal val;
            if (values != null && values.TryGetValue(key, out value) &&
                decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out val))
                return val;
            return 0m;
        }
    }

    internal static class HostTextProtocol
    {
        // Node writes UTF-8, independent of the Windows console's OEM code page.
        internal static StreamReader CreateReader(Stream input)
        {
            return new StreamReader(input, new UTF8Encoding(false), false);
        }
    }

    /// <summary>The pet: a frameless, per-pixel transparent, always-on-top window.</summary>
    internal sealed class PetForm : Form
    {
        private AccountData accountData = new AccountData();
        private ToolStripMenuItem accountMenuItem;
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
        private Bitmap frame;
        private Bitmap spriteMask;
        private readonly SatietyMeter satiety;
        private ToolStripMenuItem satietyMenuItem;
        private long lastSatietySave;
        private bool fileHover;
        private int hoverBob;
        private float hoverRotation;
        private bool feedingBusy;
        private bool closeAfterFeeding;
        private DateTime eatingUntil = DateTime.MinValue;
        private DateTime eatingSince = DateTime.MinValue;
        private bool eatingVisible;
        private MiniPanel miniPanel;
        private NotifyIcon trayIcon;
        internal bool HasHostPipe;

        private bool isTaskRunning = false;
        private DateTime lastTaskFinished = DateTime.MinValue;
        private static readonly PetState[] PostTaskStates = new PetState[] { PetState.Playing, PetState.Slacking, PetState.Thinking };
        private int postTaskIndex = 0;
        private const int SleepAfterSeconds = 600; // 10 minutes

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
            satiety = new SatietyMeter(prefs.Satiety, prefs.SatietyElapsedMs, AwakeClock.Milliseconds);
            lastSatietySave = AwakeClock.Milliseconds;

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
            AllowDrop = true;
            DragEnter += OnFileDrag;
            DragOver += OnFileDrag;
            DragLeave += delegate { EndFileHover(); };
            DragDrop += OnFileDrop;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            satietyMenuItem = new ToolStripMenuItem { Enabled = false };
            UpdateSatiety(false);
            menu.Items.Add(satietyMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem openHarness = new ToolStripMenuItem("打开 dsh 窗口");
            openHarness.Click += delegate
            {
                try { HarnessWindow.Open(); }
                catch (Exception error) { Say("无法打开 Harness：" + error.Message, 5000); }
            };
            menu.Items.Add(openHarness);

            ToolStripMenuItem mini = new ToolStripMenuItem("迷你面板");
            mini.Click += delegate
            {
                if (miniPanel != null && !miniPanel.IsDisposed)
                {
                    miniPanel.Reveal(this);
                    return;
                }
                if (!HasHostPipe)
                {
                    MessageBox.Show("请通过 DSH 插件启动桌宠，以连接当前会话。", "迷你面板", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Console.WriteLine("pet:mini-panel");
                Console.Out.Flush();
            };
            menu.Items.Add(mini);
            menu.Items.Add(new ToolStripSeparator());

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

            accountMenuItem = new ToolStripMenuItem("账号");
            UpdateAccountMenu();
            accountMenuItem.DropDownOpening += delegate
            {
                if (HasHostPipe)
                {
                    Console.WriteLine("pet:account:query");
                    Console.Out.Flush();
                }
            };
            menu.Items.Add(accountMenuItem);

            menu.Items.Add(new ToolStripSeparator());

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
                UpdateSatiety(false);
                string[] views = { "front", "side", "back" };
                for (int i = 0; i < views.Length; i++)
                    ((ToolStripMenuItem)view.DropDownItems[i]).Checked = prefs.Pose == views[i];
                int[] sizes = { 132, 176, 232 };
                for (int i = 0; i < sizes.Length; i++)
                    ((ToolStripMenuItem)size.DropDownItems[i]).Checked = prefs.Size == sizes[i];
            };
            quit.ForeColor = Color.FromArgb(183, 94, 120);
            ContextMenuStrip = menu;

            SetupTrayAndHotkeys();
        }

        private void UpdateAccountMenu()
        {
            if (accountMenuItem == null) return;
            accountMenuItem.DropDownItems.Clear();

            if (!accountData.Authenticated)
            {
                ToolStripMenuItem statusItem = new ToolStripMenuItem("👤 状态：未登录") { Enabled = false };
                ToolStripMenuItem balanceItem = new ToolStripMenuItem("💰 余额：--") { Enabled = false };

                ToolStripMenuItem loginItem = new ToolStripMenuItem("🔑 登录账号");
                loginItem.Click += delegate
                {
                    if (!HasHostPipe)
                    {
                        MessageBox.Show("请通过 DeepSeek Harness 启动桌宠以登录账号。", "登录账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Console.WriteLine("pet:account:login");
                    Console.Out.Flush();
                    Say("正在打开登录页面，请在浏览器中完成登录~", 4000);
                };

                accountMenuItem.DropDownItems.Add(statusItem);
                accountMenuItem.DropDownItems.Add(balanceItem);
                accountMenuItem.DropDownItems.Add(new ToolStripSeparator());
                accountMenuItem.DropDownItems.Add(loginItem);
            }
            else
            {
                string displayName = string.IsNullOrEmpty(accountData.Name) ? "已登录用户" : accountData.Name;
                ToolStripMenuItem userItem = new ToolStripMenuItem("👤 用户：" + displayName);
                userItem.Click += delegate
                {
                    Say("主人好！当前登录账号：" + displayName, 4000);
                };

                string balanceText = string.Format(CultureInfo.InvariantCulture, "💰 余额：¥{0:F2}", accountData.TotalBalance);
                if (accountData.BonusBalance > 0m)
                {
                    balanceText += string.Format(CultureInfo.InvariantCulture, " (赠送 ¥{0:F2})", accountData.BonusBalance);
                }
                ToolStripMenuItem balanceItem = new ToolStripMenuItem(balanceText);
                balanceItem.Click += delegate
                {
                    if (accountData.BonusBalance > 0m)
                    {
                        Say(string.Format(CultureInfo.InvariantCulture, "主人，当前充值余额 ¥{0:F2}，活动赠送 ¥{1:F2}，随时为您待命哦~", accountData.NormalBalance, accountData.BonusBalance), 5000);
                    }
                    else
                    {
                        Say(string.Format(CultureInfo.InvariantCulture, "主人，当前可用余额还有 ¥{0:F2} 呢，随时准备为您打工！", accountData.TotalBalance), 5000);
                    }
                    if (HasHostPipe)
                    {
                        Console.WriteLine("pet:account:query");
                        Console.Out.Flush();
                    }
                };

                ToolStripMenuItem refreshItem = new ToolStripMenuItem("🔄 刷新余额");
                refreshItem.Click += delegate
                {
                    if (HasHostPipe)
                    {
                        Console.WriteLine("pet:account:query");
                        Console.Out.Flush();
                        Say("正在为您刷新余额中…", 3000);
                    }
                    else
                    {
                        Say("未连接到 DSH 服务呢~", 3000);
                    }
                };

                ToolStripMenuItem logoutItem = new ToolStripMenuItem("🚪 退出登录");
                logoutItem.ForeColor = Color.FromArgb(183, 94, 120);
                logoutItem.Click += delegate
                {
                    if (MessageBox.Show("确定要退出当前账号吗？", "退出账号", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        if (HasHostPipe)
                        {
                            Console.WriteLine("pet:account:logout");
                            Console.Out.Flush();
                        }
                        accountData = new AccountData();
                        UpdateAccountMenu();
                        Say("已退出账号。", 3000);
                    }
                };

                accountMenuItem.DropDownItems.Add(userItem);
                accountMenuItem.DropDownItems.Add(balanceItem);
                accountMenuItem.DropDownItems.Add(refreshItem);
                accountMenuItem.DropDownItems.Add(new ToolStripSeparator());
                accountMenuItem.DropDownItems.Add(logoutItem);
            }

            WhaleMenuRenderer.Style(accountMenuItem.DropDown);
        }

        internal void WakeUpAndShow()
        {
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            Location = ClampToScreen(Location);
            TopMost = prefs.TopMost;
            BringToFront();
            Activate();
            Greet();
        }

        private void SetupTrayAndHotkeys()
        {
            try
            {
                trayIcon = new NotifyIcon();
                if (current != null && current.Image != null)
                {
                    IntPtr hIcon = current.Image.GetHicon();
                    trayIcon.Icon = Icon.FromHandle(hIcon);
                }
                else
                {
                    trayIcon.Icon = SystemIcons.Application;
                }
                trayIcon.Text = "鲸鱼娘桌宠 (左键点击唤醒)";
                trayIcon.Visible = true;
                trayIcon.MouseClick += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        WakeUpAndShow();
                    }
                };

                ContextMenuStrip trayMenu = new ContextMenuStrip();
                ToolStripMenuItem tmShow = new ToolStripMenuItem("唤醒桌宠 (Alt+W)");
                tmShow.Click += delegate { WakeUpAndShow(); };
                ToolStripMenuItem tmCorner = new ToolStripMenuItem("回到右下角");
                tmCorner.Click += delegate { MoveToDefaultCorner(); WakeUpAndShow(); };
                ToolStripMenuItem tmHide = new ToolStripMenuItem("隐藏桌宠");
                tmHide.Click += delegate { Hide(); };
                ToolStripMenuItem tmQuit = new ToolStripMenuItem("彻底退出");
                tmQuit.Click += delegate { Close(); };
                trayMenu.Items.AddRange(new ToolStripItem[] { tmShow, tmCorner, tmHide, new ToolStripSeparator(), tmQuit });
                WhaleMenuRenderer.Style(trayMenu);
                trayIcon.ContextMenuStrip = trayMenu;
            }
            catch { }

            try
            {
                RegisterHotKey(Handle, HOTKEY_ID, MOD_ALT, VK_W);
            }
            catch { }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (miniPanel != null && !miniPanel.IsDisposed && miniPanel.Visible)
            {
                miniPanel.UpdatePosition(this);
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && miniPanel != null && !miniPanel.IsDisposed && miniPanel.Visible)
            {
                miniPanel.Hide();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            base.OnFormClosed(e);
        }

        internal void ReceiveHostMessage(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            string trimmed = line.Trim();
            if (trimmed == "wake" || trimmed.StartsWith("wake:", StringComparison.Ordinal))
            {
                WakeUpAndShow();
                if (trimmed.StartsWith("wake:", StringComparison.Ordinal))
                    Console.WriteLine("pet:awake:" + trimmed.Substring(5));
                return;
            }
            if (trimmed == "task:start" || trimmed == "state:working")
            {
                StartTask();
                return;
            }
            if (trimmed == "task:end" || trimmed == "state:idle")
            {
                EndTask();
                return;
            }
            if (trimmed.StartsWith("account:info:", StringComparison.Ordinal))
            {
                string json = trimmed.Substring("account:info:".Length);
                accountData = AccountData.Parse(json);
                UpdateAccountMenu();
                return;
            }
            if (line.StartsWith("panel-error:", StringComparison.Ordinal))
            {
                MessageBox.Show("DSH 会话服务尚未就绪，请稍后重试。", "迷你面板");
                return;
            }
            if (!line.StartsWith("panel:", StringComparison.Ordinal)) return;
            if (miniPanel == null || miniPanel.IsDisposed) miniPanel = new MiniPanel(line.Substring(6), this);
            miniPanel.Reveal(this);
        }

        internal void StartTask()
        {
            isTaskRunning = true;
            lastActive = DateTime.Now;
            SetActivity(PetState.Working);
        }

        internal void EndTask()
        {
            isTaskRunning = false;
            lastTaskFinished = DateTime.Now;
            lastActive = DateTime.Now;
            stateSince = DateTime.Now;
            postTaskIndex = 0;
            SetActivity(PetState.Playing);
        }

        private static string Label(string pose)
        {
            if (pose == "front") return "正面";
            if (pose == "side") return "侧面";
            return "背面";
        }

        private void LoadPoses()
        {
            foreach (string name in new string[] { "working", "playing", "slacking", "thinking", "sleep", "eating" })
            {
                string path = Path.Combine(artDirectory, name + ".png");
                if (File.Exists(path))
                    poses[name] = new Pose(name, path, 0, 0, 0, 0, 0);
            }
            // Backward-compatibility fallback
            foreach (string legacy in new string[] { "daydreaming", "bored" })
            {
                string path = Path.Combine(artDirectory, legacy + ".png");
                if (File.Exists(path) && !poses.ContainsKey(legacy))
                    poses[legacy] = new Pose(legacy, path, 0, 0, 0, 0, 0);
            }
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

        private Rectangle BaselineSpriteBounds()
        {
            Pose basePose = poses.ContainsKey("front") ? poses["front"] : current;
            if (basePose == null || basePose.Image == null) return VisibleSpriteBounds();
            float scale = Math.Min(prefs.Size / (float)basePose.Image.Height, (ClientSize.Width - 64f) / basePose.Image.Width);
            int width = (int)Math.Round(basePose.Image.Width * scale);
            int height = (int)Math.Round(basePose.Image.Height * scale);
            int left = (ClientSize.Width - width) / 2, top = ClientSize.Height - height - 20;
            Rectangle source = basePose.VisibleBounds;
            return Rectangle.FromLTRB(left + (int)Math.Floor(source.Left * width / (double)basePose.Image.Width),
                top + (int)Math.Floor(source.Top * height / (double)basePose.Image.Height),
                left + (int)Math.Ceiling(source.Right * width / (double)basePose.Image.Width),
                top + (int)Math.Ceiling(source.Bottom * height / (double)basePose.Image.Height));
        }

        internal Rectangle StableAnchorBounds
        {
            get
            {
                Rectangle v = BaselineSpriteBounds();
                return new Rectangle(Location.X + v.Left, Location.Y + v.Top, v.Width, v.Height);
            }
        }

        internal Rectangle ScreenSpriteBounds
        {
            get { return StableAnchorBounds; }
        }

        private void RememberPosition()
        {
            UpdateSatiety(false);
            prefs.X = Location.X;
            prefs.Y = Location.Y;
            prefs.Save();
        }

        private void SaveAndInvalidate()
        {
            UpdateSatiety(false);
            prefs.Save();
            Invalidate();
        }

        // ------------------------------------------------------------ behaviour

        private void SetPose(string name)
        {
            if (!poses.ContainsKey(name)) return;
            prefs.Pose = name;
            current = DateTime.Now < eatingUntil && poses.ContainsKey("eating") ? poses["eating"] : poses[name];
            ApplySize();
            Touch();
            Say(name == "front" ? "正面登场！" : name == "side" ? "从这边看也好看～" : "看我的鲸鱼尾巴！", 2000);
            SaveAndInvalidate();
            if (miniPanel != null && !miniPanel.IsDisposed && miniPanel.Visible) miniPanel.UpdatePosition(this);
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
            stateSince = lastActive;
            bubble = null;
        }

        private static string StateLabel(PetState value)
        {
            return new string[] { "普通", "工作 (奋笔疾书)", "玩耍 (欢快奔跑)", "摸鱼 (抱抱玩偶)", "思考 (云状气泡)", "睡觉 (盖被安睡)" }[(int)value];
        }

        private void SetActivity(PetState value)
        {
            activity = value;
            SelectDisplayPose();
            ApplySize();
            RenderFrame();
            if (miniPanel != null && !miniPanel.IsDisposed && miniPanel.Visible) miniPanel.UpdatePosition(this);
        }

        private void SelectDisplayPose()
        {
            string key = activity.ToString().ToLowerInvariant();
            eatingVisible = DateTime.Now < eatingUntil && poses.ContainsKey("eating");
            if (eatingVisible) current = poses["eating"];
            else if (activity != PetState.Normal && poses.ContainsKey(key)) current = poses[key];
            else current = poses.ContainsKey(prefs.Pose) ? poses[prefs.Pose] : poses["front"];
        }

        private void AdvanceActivity(DateTime now)
        {
            UpdateSatiety(true);
            if (dragging || fileHover || (ContextMenuStrip != null && ContextMenuStrip.Visible)) return;

            // 1. DSH 任务执行中：固定为工作状态（侧后方视角，奋笔疾书）
            if (isTaskRunning)
            {
                if (activity != PetState.Working)
                {
                    SetActivity(PetState.Working);
                    stateSince = now;
                }
                return;
            }

            // 2. 任务结束后 / 用户闲置状态检测
            double secondsSinceActive = (now - lastActive).TotalSeconds;

            // 2.1 超过 10 分钟 (600秒) 无操作且无任务：切换为 sleep (盖着被子睡觉)
            if (secondsSinceActive >= SleepAfterSeconds)
            {
                if (activity != PetState.Sleep)
                {
                    SetActivity(PetState.Sleep);
                    stateSince = now;
                }
                return;
            }

            // 2.2 10 分钟内活跃期：在玩耍 (Playing)、摸鱼 (Slacking)、思考 (Thinking) 之间轮播切换
            if (activity == PetState.Sleep || activity == PetState.Working || (now - stateSince).TotalSeconds >= 18)
            {
                PetState next = PostTaskStates[postTaskIndex++ % PostTaskStates.Length];
                SetActivity(next);
                stateSince = now;
            }
        }

        // -------------------------------------------------------------- pointer

        private void UpdateSatiety(bool persist)
        {
            if (satiety == null) return;
            long now = AwakeClock.Milliseconds;
            int before = satiety.Value;
            satiety.Advance(now);
            prefs.Satiety = satiety.Value;
            prefs.SatietyElapsedMs = (int)satiety.ElapsedMs;
            if (satietyMenuItem != null) satietyMenuItem.Text = "饱腹值：" + satiety.Value + "/100";
            if (persist && (before != satiety.Value || now - lastSatietySave >= 60000))
            {
                prefs.Save();
                lastSatietySave = now;
            }
        }

        private void EndFileHover()
        {
            fileHover = false;
            RenderFrame();
        }

        private string[] GetDropPaths(DragEventArgs e)
        {
            try
            {
                return e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop, false)
                    ? FileFeeding.Normalize(e.Data.GetData(DataFormats.FileDrop, false) as string[]) : new string[0];
            }
            catch { return new string[0]; }
        }

        private void OnFileDrag(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (feedingBusy || dragging) return;
            string[] paths = GetDropPaths(e);
            if (paths.Length == 0) return;
            if (!fileHover)
            {
                hoverBob = BobOffset();
                hoverRotation = SpriteRotation();
                fileHover = true;
                RenderFrame();
            }
            bool hasFile = false;
            foreach (string path in paths) if (FileFeeding.IsRegularLocalFile(path)) { hasFile = true; break; }
            if (hasFile && IsSpriteAt(PointToClient(new Point(e.X, e.Y)))
                && (e.AllowedEffect & DragDropEffects.Move) != 0)
                e.Effect = DragDropEffects.Move;
        }

        private bool IsSpriteAt(Point point)
        {
            return spriteMask != null && point.X >= 0 && point.Y >= 0
                && point.X < spriteMask.Width && point.Y < spriteMask.Height
                && spriteMask.GetPixel(point.X, point.Y).A > 24;
        }

        private void OnFileDrop(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            string[] paths = GetDropPaths(e);
            bool accept = !feedingBusy && !dragging && paths.Length > 0
                && (e.AllowedEffect & DragDropEffects.Move) != 0
                && IsSpriteAt(PointToClient(new Point(e.X, e.Y)));
            EndFileHover();
            if (!accept) return;
            feedingBusy = true;
            // Shell work needs its own STA, leaving the UI and host pipe responsive.
            System.Threading.Thread worker = new System.Threading.Thread(delegate()
            {
                FeedingResult result = FileFeeding.Process(paths, RecycleBin.Recycle);
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((Action)delegate { CompleteFeeding(result); });
                }
                catch (InvalidOperationException) { }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(System.Threading.ApartmentState.STA);
            worker.Start();
            // Files are recycled here, not moved by the drag source. Returning None on Drop
            // prevents a source from trying to remove them a second time after our async work.
        }

        private void CompleteFeeding(FeedingResult result)
        {
            feedingBusy = false;
            UpdateSatiety(false);
            if (result.Successful > 0)
            {
                int gained = satiety.Feed(result.Successful);
                UpdateSatiety(false);
                prefs.Save();
                Touch();
                eatingSince = DateTime.Now;
                eatingUntil = eatingSince.AddSeconds(3);
                SelectDisplayPose();
                RenderFrame();
                Say("吃掉 " + result.Successful + " 个文件～\n饱腹 +" + gained + "（" + satiety.Value + "/100）"
                    + (result.Failed > 0 ? "\n" + result.Failed + " 个未回收" : ""), 5000);
            }
            else Say("没吃到～文件未回收（" + result.Failed + " 个）", 5000);
            foreach (string error in result.Errors) Console.Error.WriteLine("[pet] feeding: " + error);
            if (result.Failed > 0 && (!prefs.Speech || result.Successful == 0) && trayIcon != null)
                trayIcon.ShowBalloonTip(5000, "文件投喂", result.Failed + " 个文件未回收。仅接受可回收的本地文件。", ToolTipIcon.Warning);
            if (closeAfterFeeding) Close();
        }

        private void OnPetMouseDown(object sender, MouseEventArgs e)
        {
            lastActive = DateTime.Now;
            if (activity == PetState.Sleep)
            {
                postTaskIndex = 0;
                SetActivity(PetState.Playing);
                Say("呼噜……唔，醒啦！", 2000);
            }
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
            lastActive = DateTime.Now;
            if (activity == PetState.Sleep)
            {
                SetActivity(PetState.Playing);
            }
            else
            {
                SetActivity(PetState.Normal);
            }
        }

        /// <summary>Per-pixel hit testing: the window only reacts where she is opaque.</summary>
        protected override void WndProc(ref Message message)
        {
            if ((WakeWindowMessage != 0 && message.Msg == (int)WakeWindowMessage)
                || (message.Msg == 0x0312 && message.WParam.ToInt32() == HOTKEY_ID))
            {
                WakeUpAndShow();
                message.Result = new IntPtr(1);
                return;
            }
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
            if (fileHover) return hoverBob;
            if (dragging || NearScreenEdge()) return 0;
            if (eatingVisible) return -(int)Math.Round(5 * Math.Abs(Math.Sin((DateTime.Now - eatingSince).TotalSeconds * 5)));
            double phase = Environment.TickCount / 1000.0;
            if (activity == PetState.Playing) return -(int)Math.Round(12 * Math.Abs(Math.Sin(phase * 2.8)));
            if (activity == PetState.Working) return (int)Math.Round(1.5 * Math.Sin(phase * 4.0));
            if (activity == PetState.Sleep) return (int)Math.Round(1.5 * Math.Sin(phase * 0.8));
            if (activity == PetState.Thinking) return (int)Math.Round(2 * Math.Sin(phase * 1.2));
            if (activity == PetState.Slacking) return (int)Math.Round(2 * Math.Sin(phase * 1.0));
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

        private float SpriteRotation()
        {
            if (fileHover) return hoverRotation;
            if (dragging || eatingVisible || NearScreenEdge()) return 0f;
            double phase = (DateTime.Now - stateSince).TotalSeconds;
            if (activity == PetState.Thinking) return (float)(Math.Sin(phase * 1.2) * 1.2);
            if (activity == PetState.Slacking) return (float)(1.5 * Math.Sin(phase));
            if (activity == PetState.Playing) return (float)(4 * Math.Sin(phase * 3));
            if (activity == PetState.Working) return (float)(0.6 * Math.Sin(phase * 5));
            return 0f;
        }

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
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] internal static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        internal static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const int HOTKEY_ID = 0x5748;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_W = 0x57;
        internal static uint WakeWindowMessage;

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
            if (eatingVisible && DateTime.Now >= eatingUntil && !fileHover) SelectDisplayPose();
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
            // 立绘上方不再显示任何状态胶囊与名称（根据需求1）
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

            float rotation = SpriteRotation();
            double phase = (DateTime.Now - stateSince).TotalSeconds;

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

            if (spriteMask == null || spriteMask.Size != ClientSize)
            {
                if (spriteMask != null) spriteMask.Dispose();
                spriteMask = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics mask = Graphics.FromImage(spriteMask))
            {
                mask.Clear(Color.Transparent);
                mask.InterpolationMode = graphics.InterpolationMode;
                mask.PixelOffsetMode = graphics.PixelOffsetMode;
                if (Math.Abs(rotation) > 0.01f)
                {
                    mask.TranslateTransform(left + width / 2f, drawTop + height);
                    mask.RotateTransform(rotation);
                    mask.TranslateTransform(-(left + width / 2f), -(drawTop + height));
                }
                mask.DrawImage(current.Image, new Rectangle(left, drawTop, width, height));
            }

            if (eatingVisible)
            {
                double elapsed = (DateTime.Now - eatingSince).TotalSeconds;
                using (Font heartFont = new Font("Segoe UI Symbol", 13f))
                for (int i = 0; i < 5; i++)
                {
                    double progress = (elapsed * 0.65 + i * 0.2) % 1.0;
                    float x = left + width * (0.2f + i * 0.15f);
                    float y = top + height * 0.48f - (float)progress * 50;
                    using (Brush brush = new SolidBrush(Color.FromArgb((int)(200 * (1 - progress)), 249, 123, 160)))
                        graphics.DrawString("♥", heartFont, brush, x, y);
                }
            }

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
                int paddingX = 9;
                int paddingY = 5;
                // Wrap long lines to the layered window's width; anything wider is clipped by the window.
                int maxTextWidth = Math.Max(60, ClientSize.Width - 8 - paddingX * 2);
                SizeF size = graphics.MeasureString(text, font, maxTextWidth);
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

                // Little tail pointing at her head, kept under the bubble body.
                int tailX = Math.Max(left + 12, Math.Min(left + bubbleWidth - 12, centerX));
                Point[] tail = new Point[]
                {
                    new Point(tailX - 4, top + bubbleHeight - 1),
                    new Point(tailX + 4, top + bubbleHeight - 1),
                    new Point(tailX, top + bubbleHeight + 6)
                };
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 21, 26, 40)))
                {
                    graphics.FillPolygon(fill, tail);
                }

                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(255, 238, 242, 251)))
                {
                    graphics.DrawString(text, font, textBrush, new RectangleF(left + paddingX, top + paddingY, size.Width + 1, size.Height + 1));
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
            if (feedingBusy)
            {
                closeAfterFeeding = true;
                e.Cancel = true;
                Say("正在回收文件，完成后退出～", 3000);
                return;
            }
            RememberPosition();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animation != null) animation.Dispose();
                if (frame != null) frame.Dispose();
                if (spriteMask != null) spriteMask.Dispose();
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

        [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder name, int length, out int needed);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        private static string ObjectName(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return "Default";
            try
            {
                var name = new StringBuilder(256);
                int needed;
                if (!GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out needed))
                    return "Default";
                return name.ToString();
            }
            catch { return "Default"; }
        }

        private static string InstanceName()
        {
            try
            {
                string desktop = ObjectName(GetProcessWindowStation()) + "\\" + ObjectName(GetThreadDesktop(GetCurrentThreadId()));
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    return @"Local\DSH.WhalePet." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(desktop))).Replace("-", "");
            }
            catch
            {
                return @"Local\DSH.WhalePet.Default";
            }
        }

        private static bool WakeExisting()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            do
            {
                IntPtr window = FindWindow(null, "鲸鱼娘");
                IntPtr result;
                if (window != IntPtr.Zero && SendMessageTimeout(window, PetForm.WakeWindowMessage,
                    IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out result) != IntPtr.Zero && result == new IntPtr(1))
                    return true;
                System.Threading.Thread.Sleep(100);
            } while (DateTime.UtcNow < deadline);
            return false;
        }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        private static void EnableHighDpi()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10 && SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    return;
            }
            catch { }
            try
            {
                // PROCESS_PER_MONITOR_DPI_AWARE = 2
                SetProcessDpiAwareness(2);
                return;
            }
            catch { }
            try
            {
                SetProcessDPIAware();
            }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
                EnableHighDpi();
                PetForm.WakeWindowMessage = PetForm.RegisterWindowMessage("DSH_WHALE_PET_WAKE_MESSAGE");
                bool created;
                using (var instance = new System.Threading.Mutex(true, InstanceName(), out created))
                {
                    if (!created)
                    {
                        // Report success only after the existing UI thread acknowledges showing the window.
                        if (WakeExisting()) Console.WriteLine("pet:forwarded");
                        else
                        {
                            Console.Error.WriteLine("Existing pet did not acknowledge wake-up on this desktop");
                            Environment.ExitCode = 1;
                        }
                        return;
                    }
                    try { Run(args); }
                    finally { instance.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FATAL MAIN EXCEPTION] " + ex);
                Environment.ExitCode = 1;
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
                            using (StreamReader input = HostTextProtocol.CreateReader(Console.OpenStandardInput()))
                            {
                                string line;
                                while ((line = input.ReadLine()) != null)
                                {
                                    string message = line;
                                    if (!form.IsDisposed && form.IsHandleCreated)
                                        form.BeginInvoke(new Action(delegate { if (!form.IsDisposed) form.ReceiveHostMessage(message); }));
                                }
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
                    Console.WriteLine("pet:ready");
                    if (hostPipe)
                    {
                        Console.WriteLine("pet:account:query");
                        Console.Out.Flush();
                    }
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
