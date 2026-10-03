using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace WhalePet
{
    internal static class VisualCheck
    {
        static object Call(PetForm form, string method, params object[] args)
        {
            return typeof(PetForm).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, args);
        }
        static PetState State(PetForm form)
        {
            return (PetState)typeof(PetForm).GetField("activity", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        }
        static void Assert(bool condition, string text) { if (!condition) throw new Exception(text); }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview");
            Directory.CreateDirectory(output);
            using (PetForm form = new PetForm())
            using (Bitmap sheet = new Bitmap(900, 680))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(231, 237, 245));
                int index = 0;
                foreach (PetState state in Enum.GetValues(typeof(PetState)))
                {
                    Call(form, "SetActivity", state);
                    using (Bitmap image = (Bitmap)Call(form, "CreateFrame"))
                    {
                        Assert(image.GetPixel(0, 0).A == 0 && image.GetPixel(image.Width - 1, image.Height - 1).A == 0, "Opaque background");
                        int transparent = 0, solid = 0, partial = 0;
                        for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < image.Width; x++)
                        {
                            byte a = image.GetPixel(x, y).A;
                            if (a == 0) transparent++; else if (a == 255) solid++; else partial++;
                        }
                        Assert(transparent > image.Width * image.Height / 3 && solid > 100 && partial > 100, "Missing alpha or artwork");
                        image.Save(Path.Combine(output, state + ".png"), ImageFormat.Png);
                        int left = index % 3 * 300, top = index / 3 * 340;
                        using (Brush tile = new SolidBrush(Color.FromArgb(211, 221, 235)))
                            for (int y = 0; y < 320; y += 16)
                            for (int x = 0; x < 288; x += 16)
                                if ((x / 16 + y / 16) % 2 == 0) g.FillRectangle(tile, left + x, top + y, 16, 16);
                        g.DrawImageUnscaled(image, left + (288 - image.Width) / 2, top + 20);
                    }
                    Call(form, "OnPetMouseDown", form, new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                    Assert(State(form) == PetState.Normal, "Click did not reset " + state);
                    Call(form, "OnPetMouseUp", form, new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
                    index++;
                }
                sheet.Save(Path.Combine(output, "states.png"), ImageFormat.Png);
                DateTime now = DateTime.Now.AddSeconds(13);
                for (int i = 1; i <= 5; i++)
                {
                    Call(form, "AdvanceActivity", now);
                    Assert(State(form) == (PetState)i, "Idle cycle skipped state " + i);
                    now = now.AddSeconds(11);
                }
                // Exercise real HWND composition, rather than bitmap export alone.
                form.Show();
                Call(form, "RenderFrame");
                Assert(form.Visible, "Layered window not visible");
                form.ContextMenuStrip.Show(new Point(100, 100));
                Application.DoEvents();
                using (Bitmap menu = new Bitmap(form.ContextMenuStrip.Width, form.ContextMenuStrip.Height))
                {
                    form.ContextMenuStrip.DrawToBitmap(menu, new Rectangle(Point.Empty, menu.Size));
                    menu.Save(Path.Combine(output, "menu.png"), ImageFormat.Png);
                }
                form.ContextMenuStrip.Close();
                form.Close();
            }
            Console.WriteLine("PASS: six ARGB frames, click reset from every state, idle cycle and native layered window");
        }
    }
}
