using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace WhalePet
{
    internal static class AccountCheck
    {
        [STAThread]
        private static void Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            // Reproduce a Chinese Windows machine's non-UTF-8 console default.
            Console.InputEncoding = Encoding.GetEncoding(936);
            string[] names = { "鲸鱼娘测试", "张三\"小鲸\"\\目录", "海豚🐳 café", "已登录用户", "中文\\u0061" };
            using (StreamReader input = HostTextProtocol.CreateReader(Console.OpenStandardInput()))
            using (PetForm form = new PetForm())
            {
                foreach (string expected in names)
                {
                    string line = input.ReadLine();
                    if (line == null) throw new Exception("Missing fixture input");
                    form.ReceiveHostMessage(line);
                    var data = (AccountData)typeof(PetForm).GetField("accountData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    if (!data.Authenticated || data.Name != expected || data.TotalBalance != 12.50m)
                        throw new Exception("UTF-8/JSON account data mismatch");
                    var menu = (ToolStripMenuItem)typeof(PetForm).GetField("accountMenuItem", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    if (menu.DropDownItems[0].Text != "👤 用户：" + expected)
                        throw new Exception("Account menu text mismatch");
                }
            }
            if (AccountData.Parse("{broken").Authenticated || AccountData.Parse("null").Authenticated)
                throw new Exception("Malformed data must not authenticate");
            var loggedOut = AccountData.Parse("{\"authenticated\":false,\"user\":null,\"balance\":null}");
            if (loggedOut.Authenticated || loggedOut.Name != "" || loggedOut.TotalBalance != 0m)
                throw new Exception("Logged-out defaults mismatch");
            Console.WriteLine("PASS: UTF-8 host pipe under CP936, Unicode/escaped names, balance and actual account menu labels");
        }
    }
}
