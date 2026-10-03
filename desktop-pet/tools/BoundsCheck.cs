using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
namespace WhalePet
{
    internal static class BoundsCheck
    {
        [STAThread]
        static void Main()
        {
            using (PetForm form = new PetForm())
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Prefs prefs = (Prefs)typeof(PetForm).GetField("prefs", flags).GetValue(form);
                foreach (int size in new int[] {132, 176, 232})
                foreach (string pose in new string[] {"front", "side", "back"})
                foreach (PetState state in Enum.GetValues(typeof(PetState)))
                {
                    prefs.Size = size; prefs.Pose = pose;
                    typeof(PetForm).GetMethod("SetActivity", flags).Invoke(form, new object[] { state });
                    Rectangle visible = (Rectangle)typeof(PetForm).GetMethod("VisibleSpriteBounds", flags).Invoke(form, null);
                    foreach (Rectangle area in new Rectangle[] {new Rectangle(0,0,1920,1040), new Rectangle(-1920,-1080,1920,1040)})
                    {
                        Point first = PetForm.ClampVisible(new Point(-10000,-10000), visible, area);
                        Point last = PetForm.ClampVisible(new Point(10000,10000), visible, area);
                        if (first.X + visible.Left != area.Left || first.Y + visible.Top != area.Top
                            || last.X + visible.Right != area.Right || last.Y + visible.Bottom != area.Bottom)
                            throw new Exception("Visible contour did not reach screen edge");
                        Point center = new Point(area.Left + 400, area.Top + 400);
                        if (PetForm.ClampVisible(center, visible, area) != center) throw new Exception("Interior movement changed");
                    }
                }
            }
            Console.WriteLine("PASS: 108 pose/size/state/monitor combinations, four edges and free interior movement");
        }
    }
}
