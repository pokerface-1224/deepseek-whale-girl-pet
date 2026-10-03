using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace WhalePet
{
    internal static class FeedingCheck
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static int assertions;
        private static void Assert(bool value, string message)
        {
            assertions++;
            if (!value) throw new Exception(message);
        }
        private static object Call(PetForm form, string method, params object[] args)
        { return typeof(PetForm).GetMethod(method, Private).Invoke(form, args); }
        private static object Field(PetForm form, string name)
        { return typeof(PetForm).GetField(name, Private).GetValue(form); }
        private static void Set(PetForm form, string name, object value)
        { typeof(PetForm).GetField(name, Private).SetValue(form, value); }

        [STAThread]
        private static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception error)
            {
                while (error.InnerException != null) error = error.InnerException;
                Console.Error.WriteLine("FAIL after " + assertions + " checks: " + error.GetType().Name + ": " + error.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string fixtures = Path.Combine(root, "fixtures");
            Directory.CreateDirectory(fixtures);
            string one = Path.Combine(fixtures, "投喂-a.txt");
            string two = Path.Combine(fixtures, new string('b', 120) + ".txt");
            string missing = Path.Combine(fixtures, "missing.txt");
            File.WriteAllText(one, "first");
            File.WriteAllText(two, "second");

            SatietyMeter meter = new SatietyMeter(60, 299000, 1000);
            meter.Advance(1999);
            Assert(meter.Value == 60 && meter.ElapsedMs == 299999, "Early decay");
            meter.Advance(2000);
            Assert(meter.Value == 59 && meter.ElapsedMs == 0, "Boundary decay");
            meter.Advance(602123);
            Assert(meter.Value == 57 && meter.ElapsedMs == 123, "Elapsed accumulation");
            Assert(meter.Feed(2) == 20 && meter.Value == 77, "Multi-file reward");
            Assert(meter.Feed(9) == 23 && meter.Feed(1) == 0, "Cap/full feeding");
            Assert(meter.Feed(-1) == 0, "Negative count");
            SatietyMeter resumed = new SatietyMeter(meter.Value, meter.ElapsedMs, 9999999);
            resumed.Advance(9999999);
            Assert(resumed.Value == 100 && resumed.ElapsedMs == 123, "Offline time charged");
            resumed.Advance(9999999 + 300000 * 150L);
            Assert(resumed.Value == 0, "Floor");
            Assert(new SatietyMeter(200, -1, 0).Value == 100 && new SatietyMeter(-10, 999999, 0).ElapsedMs == 299999, "Bounds");
            long awake = AwakeClock.Milliseconds;
            Assert(awake > 0 && AwakeClock.Milliseconds >= awake, "Awake clock");

            int calls = 0;
            FeedingResult result = FileFeeding.Process(new string[] { one, one.ToUpperInvariant(), two, fixtures, missing },
                delegate(string path) { calls++; if (path == two) throw new UnauthorizedAccessException("test denial"); return true; });
            Assert(result.Successful == 1 && result.Failed == 3 && calls == 2, "Dedup/partial failures/directories/missing");
            Assert(FileFeeding.Normalize(new string[] { "relative.txt", null, "" }).Length == 0, "Invalid input");
            Assert(!FileFeeding.IsRegularLocalFile(@"\\server\share\file.txt"), "UNC accepted");
            result = FileFeeding.Process(new string[] { one }, delegate { return false; });
            Assert(result.Successful == 0 && File.Exists(one), "Failure counted/deleted");
            RecycleBin.RecycleSink sink = new RecycleBin.RecycleSink();
            Assert(sink.PreDeleteItem(0, null) < 0 && sink.PreDeleteItem(0x80, null) == 0, "Permanent delete not vetoed");
            sink.PostDeleteItem(0x80, null, 0, null);
            Assert(!sink.Recycled, "Success without recycle item counted");

            string prefsPath = Path.Combine(root, "pet-prefs.txt");
            File.WriteAllText(prefsPath, "size=176\nx=100\ny=100\n");
            Prefs prefs = Prefs.Load();
            Assert(prefs.Satiety == 60 && prefs.SatietyElapsedMs == 0, "Legacy prefs");
            prefs.Satiety = 73; prefs.SatietyElapsedMs = 123456; prefs.Save();
            prefs = Prefs.Load();
            Assert(prefs.Satiety == 73 && prefs.SatietyElapsedMs == 123456, "Persistence");
            File.WriteAllText(prefsPath, "satiety=-8\nsatietyElapsedMs=999999\nx=100\ny=100");
            Assert(Prefs.Load().Satiety == 0 && Prefs.Load().SatietyElapsedMs == 299999, "Prefs clamp");
            File.WriteAllText(prefsPath, "size=176\nx=100\ny=100\nsatiety=60");

            using (Bitmap eating = new Bitmap(Path.Combine(root, "art", "eating.png")))
            {
                Assert(eating.GetPixel(0, 0).A == 0 && eating.GetPixel(eating.Width - 1, eating.Height - 1).A == 0, "Art not transparent");
            }
            using (PetForm form = new PetForm())
            using (Bitmap sheet = new Bitmap(900, 360))
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.FromArgb(229, 237, 246));
                Prefs live = (Prefs)Field(form, "prefs");
                form.StartTask();
                Call(form, "CompleteFeeding", new FeedingResult { Successful = 2, Failed = 1 });
                Assert((PetState)Field(form, "activity") == PetState.Working && ((Pose)Field(form, "current")).Name == "eating", "Feeding overwrote task");
                Assert(live.Satiety == 80, "Wrong successful count");
                form.EndTask();
                Assert((PetState)Field(form, "activity") == PetState.Playing && ((Pose)Field(form, "current")).Name == "eating", "Host end interrupted eating");
                Set(form, "eatingUntil", DateTime.Now.AddSeconds(-1));
                Call(form, "RenderFrame");
                Assert(((Pose)Field(form, "current")).Name == "playing", "Overlay expiry");
                form.StartTask();
                Call(form, "CompleteFeeding", new FeedingResult { Successful = 1 });
                Set(form, "eatingUntil", DateTime.Now.AddSeconds(-1));
                Call(form, "RenderFrame");
                Assert(((Pose)Field(form, "current")).Name == "working", "Working restore");
                live.Speech = false;
                Call(form, "CompleteFeeding", new FeedingResult { Successful = 1 });
                Assert((bool)Field(form, "eatingVisible") && live.Satiety == 100, "Muted feed/full cap");
                Call(form, "CompleteFeeding", new FeedingResult { Successful = 1 });
                Assert(live.Satiety == 100, "Full feeding");

                int tile = 0;
                foreach (int size in new int[] { 132, 176, 232 })
                {
                    live.Size = size;
                    live.Speech = true;
                    Call(form, "ApplySize");
                    Set(form, "fileHover", true);
                    Set(form, "eatingUntil", DateTime.Now.AddMinutes(1));
                    Call(form, "SelectDisplayPose");
                    Call(form, "Say", "饱腹 +10（100/100）", 5000);
                    using (Bitmap image = (Bitmap)Call(form, "CreateFrame"))
                    {
                        image.Save(Path.Combine(root, "eating-" + size + ".png"), ImageFormat.Png);
                        graphics.DrawImageUnscaled(image, tile * 300 + (300 - image.Width) / 2, 10);
                        Bitmap mask = (Bitmap)Field(form, "spriteMask");
                        int character = 0, decoration = 0;
                        bool clipped = false;
                        for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < image.Width; x++)
                        {
                            if (mask.GetPixel(x, y).A > 24)
                            {
                                character++;
                                if (character == 1) Assert((bool)Call(form, "IsSpriteAt", new Point(x, y)), "Character rejected");
                                if (x == 0 || y == 0 || x == image.Width - 1 || y == image.Height - 1) clipped = true;
                            }
                            else if (image.GetPixel(x, y).A > 24)
                            {
                                decoration++;
                                if (decoration == 1) Assert(!(bool)Call(form, "IsSpriteAt", new Point(x, y)), "Decoration accepted");
                            }
                        }
                        Assert(character > 500 && decoration > 50, "Missing art/decorations");
                        Assert(!clipped, "Sprite clipped");
                        Assert(!(bool)Call(form, "IsSpriteAt", Point.Empty), "Background accepted");
                        Assert((int)Call(form, "BobOffset") == 0, "Drop bob not frozen");
                        live.Speech = false;
                        Set(form, "eatingUntil", DateTime.Now.AddSeconds(-1));
                        Set(form, "fileHover", false);
                        Call(form, "SetActivity", PetState.Sleep);
                    }
                    tile++;
                }

                // Exercise Drop rejection and async busy guard without ever recycling a user file.
                DataObject text = new DataObject(DataFormats.Text, one);
                DragEventArgs drag = new DragEventArgs(text, 0, 0, 0, DragDropEffects.Move, DragDropEffects.None);
                Call(form, "OnFileDrag", form, drag);
                Assert(drag.Effect == DragDropEffects.None, "Text accepted");
                DataObject files = new DataObject(DataFormats.FileDrop, new string[] { one });
                Point screen = form.PointToScreen(Point.Empty);
                drag = new DragEventArgs(files, 0, screen.X, screen.Y, DragDropEffects.Move, DragDropEffects.None);
                Call(form, "OnFileDrag", form, drag);
                Assert(drag.Effect == DragDropEffects.None && (bool)Field(form, "fileHover"), "Transparent drag accepted");
                Call(form, "OnFileDrop", form, drag);
                Assert(!(bool)Field(form, "feedingBusy") && File.Exists(one), "Transparent Drop processed");
                Set(form, "feedingBusy", true);
                Call(form, "OnFileDrag", form, drag);
                Assert(drag.Effect == DragDropEffects.None, "Busy guard");
                Set(form, "feedingBusy", false);
                sheet.Save(Path.Combine(root, "feeding-preview.png"), ImageFormat.Png);
            }

            if (args.Length > 0 && args[0] == "--recycle")
            {
                File.WriteAllText(prefsPath, "size=176\nx=100\ny=100\nsatiety=60");
                string native = Path.Combine(fixtures, "whale-feeding-" + Guid.NewGuid().ToString("N") + ".txt");
                string nativeLong = Path.Combine(fixtures, "whale-feeding-中文-" + Guid.NewGuid().ToString("N") + new string('x', 95) + ".txt");
                string nativeHtml = Path.Combine(fixtures, "whale-feeding-" + Guid.NewGuid().ToString("N") + ".html");
                string connected = Path.Combine(fixtures, Path.GetFileNameWithoutExtension(nativeHtml) + "_files");
                Directory.CreateDirectory(connected);
                string resource = Path.Combine(connected, "resource.txt");
                File.WriteAllText(resource, "must stay");
                File.WriteAllText(native, "whale feeding integration fixture");
                File.WriteAllText(nativeLong, "whale feeding integration fixture");
                File.WriteAllText(nativeHtml, "whale feeding integration fixture");
                using (PetForm form = new PetForm())
                using (FileStream locked = File.Open(one, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Prefs live = (Prefs)Field(form, "prefs");
                    live.Speech = false;
                    int before = live.Satiety;
                    Set(form, "fileHover", true);
                    Call(form, "SetActivity", PetState.Sleep);
                    Bitmap mask = (Bitmap)Field(form, "spriteMask");
                    Point target = Point.Empty;
                    for (int y = 0; y < mask.Height && target == Point.Empty; y++)
                    for (int x = 0; x < mask.Width; x++)
                        if (mask.GetPixel(x, y).A > 200) { target = new Point(x, y); break; }
                    Point screen = form.PointToScreen(target);
                    DataObject files = new DataObject(DataFormats.FileDrop, new string[] { native, nativeLong, nativeHtml, one, native, fixtures });
                    DragEventArgs drag = new DragEventArgs(files, 0, screen.X, screen.Y, DragDropEffects.Move, DragDropEffects.None);
                    Call(form, "OnFileDrag", form, drag);
                    Assert(drag.Effect == DragDropEffects.Move, "Native drag cursor did not accept character");
                    Call(form, "OnFileDrop", form, drag);
                    Assert((bool)Field(form, "feedingBusy"), "Drop did not dispatch");
                    DateTime deadline = DateTime.Now.AddSeconds(15);
                    while ((bool)Field(form, "feedingBusy") && DateTime.Now < deadline)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(10);
                    }
                    Assert(!(bool)Field(form, "feedingBusy") && !File.Exists(native) && !File.Exists(nativeLong) && !File.Exists(nativeHtml), "Async native Drop failed");
                    Assert(live.Satiety == Math.Min(100, before + 30) && File.Exists(one), "Partial async reward/locked file");
                    Assert(File.Exists(resource) && File.ReadAllText(resource) == "must stay", "HTML connected folder was recycled");
                    Assert((bool)Field(form, "eatingVisible"), "Native success missing overlay");
                }
                File.WriteAllLines(Path.Combine(root, "recycled-paths.txt"), new string[] { native, nativeLong, nativeHtml });
                using (FileStream locked = File.Open(one, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    result = FileFeeding.Process(new string[] { one }, RecycleBin.Recycle);
                    Assert(result.Successful == 0 && result.Failed == 1 && File.Exists(one), "Locked file recycled/count wrong");
                }
                Assert(!RecycleBin.Recycle(fixtures) && Directory.Exists(fixtures), "Directory processed");
            }
            Console.WriteLine("PASS: " + assertions + " feeding, clock, prefs, overlay, mask and recycle checks");
        }
    }
}
