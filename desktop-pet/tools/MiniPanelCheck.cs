using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
namespace WhalePet
{
    internal static class MiniPanelCheck
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            using (PetForm pet = new PetForm())
            {
                if (pet.ContextMenuStrip.Items[0].Text != "打开 dsh 窗口" || pet.ContextMenuStrip.Items[1].Text != "迷你面板")
                    throw new Exception("Wrong menu order");
            }
            MiniPanel panel = new MiniPanel(args[0]);
            WebView2 view = (WebView2)typeof(MiniPanel).GetField("browser", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            Timer timeout = new Timer { Interval = 25000 };
            timeout.Tick += delegate { Console.WriteLine("FAIL: " + ((Label)typeof(MiniPanel).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel)).Text); Environment.ExitCode = 1; panel.Dispose(); Application.ExitThread(); };
            view.NavigationCompleted += async delegate
            {
                try
                {
                    string title = await view.ExecuteScriptAsync("document.title");
                    if (title != "\"DSH panel fixture\"") return;
                    string result = await view.ExecuteScriptAsync("document.querySelector('input').value='hello'; document.querySelector('button').click(); document.querySelector('output').textContent");
                    if (result != "\"hello\"") throw new Exception("Browser interaction failed");
                    panel.Hide(); panel.Show();
                    if (await view.ExecuteScriptAsync("document.querySelector('output').textContent") != "\"hello\"") throw new Exception("Hide lost page state");
                    Console.WriteLine("PASS: menu ordering, actual WebView2 navigation, page interaction, hide/show state retention");
                }
                catch (Exception e) { Console.WriteLine("FAIL: " + e.GetType().Name); Environment.ExitCode = 1; }
                finally { timeout.Stop(); timeout.Dispose(); panel.Dispose(); Application.ExitThread(); }
            };
            timeout.Start();
            Application.Run(panel);
        }
    }
}
