using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>Small drawing helpers shared by the custom controls of the simulator look. Visual only.</summary>
    internal static class UiDraw
    {
        private static string monoFamily;

        /// <summary>Monospaced family of the simulator (Cascadia Mono), falling back to Consolas.</summary>
        public static string MonoFamily
        {
            get
            {
                if (monoFamily == null)
                {
                    monoFamily = "Consolas";
                    foreach (FontFamily f in FontFamily.Families)
                    {
                        if (f.Name == "Cascadia Mono") { monoFamily = f.Name; break; }
                    }
                }
                return monoFamily;
            }
        }

        /// <summary>
        /// Font of the weight readout: the simulator uses Cascadia Mono at weight 600 (SemiBold), not Bold.
        /// </summary>
        public static Font LcdFont(float pixels)
        {
            foreach (FontFamily f in FontFamily.Families)
            {
                if (f.Name == "Cascadia Mono SemiBold") return new Font(f.Name, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            return new Font(MonoFamily, pixels, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>First opaque background found walking up the parents (what is "behind" a control).</summary>
        public static Color ParentBack(Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent)
            {
                Color k = p.BackColor;
                if (k.A == 255) return k;
            }
            return Ui.Bg;
        }
    }
}
