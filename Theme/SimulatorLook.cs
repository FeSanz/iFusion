using System.Drawing;

namespace iFusion
{
    /// <summary>
    /// Re-skins the shared palette (Ui.Light / Ui.Dark) with the tokens of the scale simulator
    /// (ScaleSim.App/wwwroot/app.css), only for iFusion. Call once at startup, before any form is created.

    /// Visual only.
    /// </summary>
    internal static class SimulatorLook
    {
        public static void Apply()
        {
            Palette l = Ui.Light;
            l.Bg = Rgb(0xF4F6FA);            // --bg
            l.Surface = Rgb(0xFFFFFF);       // --panel
            l.Field = Rgb(0xEEF1F7);         // --panel-2
            l.Input = Rgb(0xF4F6FA);         // inputs sit on --bg
            l.InputInvalid = Rgb(0xFDE3E2);
            l.HeaderBack = Rgb(0xEEF1F7);
            l.Border = Rgb(0xDEDFE4);        // --border (rgba(20,30,60,.14) over white)
            l.BorderStrong = Rgb(0xC3C8D6);
            l.Text = Rgb(0x12182B);          // --text
            l.TextSecondary = Rgb(0x5D6882); // --muted
            l.Muted = Rgb(0x8B95AB);
            l.Accent = Rgb(0x2F6DF0);        // --accent
            l.AccentHover = Rgb(0x2559D0);
            l.AccentTint = Rgb(0xE6EEFE);
            l.AccentSoft = Rgb(0xCDDCFB);
            l.Danger = Rgb(0xD23340);        // --err
            l.Success = Rgb(0x1F9D57);       // --ok
            l.SuccessText = Rgb(0x1F9D57);
            l.Warning = Rgb(0xC77A07);       // --warn
            l.Disabled = Rgb(0xA8B1C2);
            l.RowWarning = Rgb(0xFFE9A8);
            l.RowDanger = Rgb(0xF8B9B6);

            Palette d = Ui.Dark;
            d.Bg = Rgb(0x11151F);            // --bg
            d.Surface = Rgb(0x1A2236);       // --panel
            d.Field = Rgb(0x222C45);         // --panel-2
            d.Input = Rgb(0x11151F);
            d.InputInvalid = Rgb(0x4A2327);
            d.HeaderBack = Rgb(0x222C45);
            d.Border = Rgb(0x2E3A55);        // --border (rgba(126,156,210,.2) over --panel)
            d.BorderStrong = Rgb(0x40507A);
            d.Text = Rgb(0xEEF2FB);
            d.TextSecondary = Rgb(0x9FADC9);
            d.Muted = Rgb(0x7B89A6);
            d.Accent = Rgb(0x4C8DFF);
            d.AccentHover = Rgb(0x6FA3FF);
            d.AccentTint = Rgb(0x1F2F55);
            d.AccentSoft = Rgb(0x2B4580);
            d.Danger = Rgb(0xFF6670);
            d.Success = Rgb(0x3CCF82);
            d.SuccessText = Rgb(0x3CCF82);
            d.Warning = Rgb(0xF0A93A);
            d.Disabled = Rgb(0x4A5570);
            d.RowWarning = Rgb(0x5C4B14);
            d.RowDanger = Rgb(0x5E2A2A);
        }

        private static Color Rgb(int rgb)
        {
            return Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
    }
}
