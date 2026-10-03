using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WhalePet
{
    /// <summary>Hosts the actual DSH web app, without copying or replacing chat logic.</summary>
    internal sealed class MiniPanel : Form
    {
        private readonly WebView2 browser;
        private readonly Label status;
        private readonly Uri endpoint;
        private bool initialized;
        private bool initializing;

        internal MiniPanel(string url)
        {
            endpoint = new Uri(url);
            if (endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1")
                throw new ArgumentException("Expected local DSH endpoint");
            Text = "鲸鱼娘 · 迷你面板";
            Size = new Size(520, 600);
            MinimumSize = new Size(360, 300);
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(246, 250, 255);
            Font = new Font("Microsoft YaHei UI", 9f);

            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
            Button reload = new Button { Text = "重新加载", AutoSize = true, FlatStyle = FlatStyle.Flat };
            Button full = new Button { Text = "打开 dsh 窗口", AutoSize = true, FlatStyle = FlatStyle.Flat };
            Button hide = new Button { Text = "收起", AutoSize = true, FlatStyle = FlatStyle.Flat };
            toolbar.Controls.AddRange(new Control[] { reload, full, hide });
            status = new Label { Dock = DockStyle.Bottom, Height = 30, Text = "正在连接 DSH…", AutoEllipsis = true };
            browser = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(browser); Controls.Add(status); Controls.Add(toolbar);
            reload.Click += async delegate { if (initialized) browser.Reload(); else await InitializeBrowser(); };
            full.Click += delegate { try { HarnessWindow.Open(); } catch { status.Text = "无法唤起 DSH 窗口"; } };
            hide.Click += delegate { Hide(); };
            Shown += async delegate { if (!initialized) await InitializeBrowser(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
        }

        private async System.Threading.Tasks.Task InitializeBrowser()
        {
            if (initializing || IsDisposed) return;
            initializing = true;
            try
            {
                // Dedicated storage, never access the desktop application's Chromium profile.
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
                    status.Text = e.IsSuccess ? "" : "连接失败，请确认 DSH 正在运行后重试";
                    status.Visible = !e.IsSuccess;
                };
                initialized = true;
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
                status.Visible = true;
                status.Text = error is UnauthorizedAccessException || error is IOException
                    ? "面板数据目录不可写，请检查插件目录权限"
                    : "面板初始化失败（" + error.GetType().Name + " / " + error.HResult.ToString("X8") + "），请检查 WebView2 Runtime";
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
}
