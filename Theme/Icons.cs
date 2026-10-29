using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace iFusion
{
    /// <summary>
    /// Line icons in the NotiLink / Feather style (24x24 grid, 2px round stroke), drawn with GDI+ so they
    /// follow the active theme color at any size. Visual only.
    /// </summary>
    internal static class Icons
    {
        public static Bitmap Render(string kind, int pixels, Color color)
        {
            pixels = Math.Max(16, pixels);
            Bitmap bmp = new Bitmap(pixels, pixels);
            using (Graphics g = Graphics.FromImage(bmp))
            using (Pen pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush fill = new SolidBrush(color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(pixels / 24f, pixels / 24f);

                switch (kind)
                {
                    case "lock":
                        g.DrawPath(pen, Round(5, 11, 14, 10, 2));
                        g.DrawArc(pen, 8, 3, 8, 8, 180, 180);
                        g.DrawLine(pen, 8, 7, 8, 11);
                        g.DrawLine(pen, 16, 7, 16, 11);
                        break;
                    case "printer":
                        g.DrawLines(pen, new[] { new PointF(6, 9), new PointF(6, 2), new PointF(18, 2), new PointF(18, 9) });
                        g.DrawPath(pen, Round(2, 9, 20, 9, 2));
                        g.DrawRectangle(pen, 6, 14, 12, 8);
                        break;
                    case "close":
                        g.DrawLine(pen, 6, 6, 18, 18);
                        g.DrawLine(pen, 18, 6, 6, 18);
                        break;
                    case "logout":
                        g.DrawLines(pen, new[] { new PointF(9, 3), new PointF(5, 3), new PointF(3, 5), new PointF(3, 19), new PointF(5, 21), new PointF(9, 21) });
                        g.DrawLines(pen, new[] { new PointF(16, 17), new PointF(21, 12), new PointF(16, 7) });
                        g.DrawLine(pen, 21, 12, 9, 12);
                        break;
                    case "swap":
                        g.DrawLines(pen, new[] { new PointF(17, 1), new PointF(21, 5), new PointF(17, 9) });
                        g.DrawLines(pen, new[] { new PointF(3, 11), new PointF(3, 9), new PointF(7, 5), new PointF(21, 5) });
                        g.DrawLines(pen, new[] { new PointF(7, 23), new PointF(3, 19), new PointF(7, 15) });
                        g.DrawLines(pen, new[] { new PointF(21, 13), new PointF(21, 15), new PointF(17, 19), new PointF(3, 19) });
                        break;
                    case "eraser":
                        g.DrawPolygon(pen, new[] { new PointF(14, 4), new PointF(21, 11), new PointF(11, 21), new PointF(4, 14) });
                        g.DrawLine(pen, 8.5f, 9.5f, 15.5f, 16.5f);
                        g.DrawLine(pen, 13, 21, 21, 21);
                        break;
                    case "refresh":
                        g.DrawArc(pen, 3, 3, 18, 18, -40, 280);
                        g.DrawLines(pen, new[] { new PointF(21, 3), new PointF(21, 9), new PointF(15, 9) });
                        break;
                    case "bag":
                        g.DrawLines(pen, new[] { new PointF(6, 2), new PointF(3, 6), new PointF(3, 20), new PointF(5, 22), new PointF(19, 22), new PointF(21, 20), new PointF(21, 6), new PointF(18, 2), new PointF(6, 2) });
                        g.DrawLine(pen, 3, 6, 21, 6);
                        g.DrawArc(pen, 8, 6, 8, 8, 0, 180);
                        break;
                    case "scale":
                        g.DrawLine(pen, 12, 3, 12, 21);
                        g.DrawLine(pen, 7, 21, 17, 21);
                        g.DrawLine(pen, 4, 7, 20, 7);
                        g.DrawLines(pen, new[] { new PointF(6, 7), new PointF(3, 14) });
                        g.DrawLines(pen, new[] { new PointF(6, 7), new PointF(9, 14) });
                        g.DrawArc(pen, 3, 11, 6, 6, 0, 180);
                        g.DrawLines(pen, new[] { new PointF(18, 7), new PointF(15, 14) });
                        g.DrawLines(pen, new[] { new PointF(18, 7), new PointF(21, 14) });
                        g.DrawArc(pen, 15, 11, 6, 6, 0, 180);
                        break;
                    case "user":
                        g.DrawEllipse(pen, 8, 3, 8, 8);
                        g.DrawLines(pen, new[] { new PointF(20, 21), new PointF(20, 19) });
                        g.DrawArc(pen, 4, 13, 16, 12, 180, 180);
                        g.DrawLine(pen, 4, 19, 4, 21);
                        break;
                    case "moon":
                        using (GraphicsPath moon = new GraphicsPath())
                        {
                            moon.AddEllipse(3, 3, 18, 18);
                            using (GraphicsPath bite = new GraphicsPath())
                            {
                                bite.AddEllipse(9, 0, 16, 16);
                                using (Region r = new Region(moon))
                                {
                                    r.Exclude(bite);
                                    g.FillRegion(fill, r);
                                }
                            }
                        }
                        break;
                    case "sun":
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI / 4 * i;
                            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                            g.DrawLine(pen, 12 + c * 7f, 12 + s * 7f, 12 + c * 10f, 12 + s * 10f);
                        }
                        break;
                }
            }
            return bmp;
        }

        private static GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            float d = r * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
