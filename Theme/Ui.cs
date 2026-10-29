using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace iFusion
{
    /// <summary>One complete set of design tokens (a theme).</summary>
    internal sealed class Palette
    {
        // Surfaces
        public Color Bg, Surface, Field, Input, InputInvalid, HeaderBack;
        // Lines
        public Color Border, BorderStrong;
        // Text
        public Color Text, TextSecondary, Muted;
        // Accent
        public Color Accent, AccentHover, AccentTint, AccentSoft;
        // Status
        public Color Danger, Success, SuccessText, Warning, Disabled;
        // Grid row highlights (weight above / below the standard)
        public Color RowWarning, RowDanger;

        private static readonly FieldInfo[] Tokens = typeof(Palette).GetFields(BindingFlags.Instance | BindingFlags.Public);

        /// <summary>If <paramref name="c"/> is one of this palette's tokens, returns the same token of <paramref name="to"/>.</summary>
        public Color Translate(Color c, Palette to)
        {
            if (c.A == 0) return c;
            foreach (FieldInfo f in Tokens)
            {
                if (((Color)f.GetValue(this)).ToArgb() == c.ToArgb())
                {
                    return (Color)f.GetValue(to);
                }
            }
            return c;
        }
    }

    /// <summary>
    /// Design tokens shared by WOW Labels / WOW Scales 2 / WOW Scales 3.
    /// Taken from the NotiLink desktop look: cool light surfaces (and its deep-navy dark mode),
    /// violet accent, soft 1px borders, small corner radius and Segoe UI typography.
    /// Light is the default; the operator's choice is remembered per app.
    /// Visual only: no business logic may depend on these values.
    /// </summary>
    internal static class Ui
    {
        public static readonly Palette Light = new Palette
        {
            Bg = Color.FromArgb(0xF5, 0xF7, 0xFA),
            Surface = Color.FromArgb(0xFC, 0xFD, 0xFF),
            Field = Color.FromArgb(0xF0, 0xF3, 0xF9),
            Input = Color.FromArgb(0xFE, 0xFE, 0xFE),
            InputInvalid = Color.FromArgb(0xFD, 0xE3, 0xE2),
            HeaderBack = Color.FromArgb(0xEE, 0xF1, 0xF8),
            Border = Color.FromArgb(0xDD, 0xE2, 0xEC),
            BorderStrong = Color.FromArgb(0xC3, 0xCD, 0xE3),
            Text = Color.FromArgb(0x1A, 0x23, 0x33),
            TextSecondary = Color.FromArgb(0x66, 0x70, 0x85),
            Muted = Color.FromArgb(0x9A, 0xA5, 0xB8),
            Accent = Color.FromArgb(0x7C, 0x5C, 0xFF),
            AccentHover = Color.FromArgb(0x6A, 0x4B, 0xEE),
            AccentTint = Color.FromArgb(0xEC, 0xE8, 0xFF),
            AccentSoft = Color.FromArgb(0xD8, 0xCF, 0xFF),
            Danger = Color.FromArgb(0xE5, 0x39, 0x35),
            Success = Color.FromArgb(0x2E, 0xCC, 0x71),
            SuccessText = Color.FromArgb(0x1E, 0x9E, 0x57),
            Warning = Color.FromArgb(0xF5, 0x9E, 0x0B),
            Disabled = Color.FromArgb(0xA8, 0xB1, 0xC2),
            RowWarning = Color.FromArgb(0xFF, 0xE9, 0xA8),
            RowDanger = Color.FromArgb(0xF8, 0xB9, 0xB6)
        };

        public static readonly Palette Dark = new Palette
        {
            Bg = Color.FromArgb(0x11, 0x15, 0x1F),
            Surface = Color.FromArgb(0x1C, 0x25, 0x3A),
            Field = Color.FromArgb(0x16, 0x1D, 0x2D),
            Input = Color.FromArgb(0x1A, 0x23, 0x33),
            InputInvalid = Color.FromArgb(0x4A, 0x23, 0x27),
            HeaderBack = Color.FromArgb(0x23, 0x2E, 0x47),
            Border = Color.FromArgb(0x2C, 0x37, 0x52),
            BorderStrong = Color.FromArgb(0x3D, 0x4B, 0x6E),
            Text = Color.FromArgb(0xEE, 0xF2, 0xFB),
            TextSecondary = Color.FromArgb(0x9F, 0xAD, 0xC9),
            Muted = Color.FromArgb(0x6F, 0x7C, 0x96),
            Accent = Color.FromArgb(0x7C, 0x5C, 0xFF),
            AccentHover = Color.FromArgb(0x8E, 0x72, 0xFF),
            AccentTint = Color.FromArgb(0x2C, 0x28, 0x58),
            AccentSoft = Color.FromArgb(0x3F, 0x38, 0x80),
            Danger = Color.FromArgb(0xEF, 0x53, 0x50),
            Success = Color.FromArgb(0x2E, 0xCC, 0x71),
            SuccessText = Color.FromArgb(0x3D, 0xDC, 0x84),
            Warning = Color.FromArgb(0xF5, 0xA5, 0x24),
            Disabled = Color.FromArgb(0x4A, 0x55, 0x70),
            RowWarning = Color.FromArgb(0x5C, 0x4B, 0x14),
            RowDanger = Color.FromArgb(0x5E, 0x2A, 0x2A)
        };

        public static Palette Current = LoadSaved() ? Dark : Light;

        public static bool IsDark { get { return ReferenceEquals(Current, Dark); } }

        // Surfaces
        public static Color Bg { get { return Current.Bg; } }
        public static Color Surface { get { return Current.Surface; } }
        public static Color Field { get { return Current.Field; } }
        public static Color Input { get { return Current.Input; } }
        public static Color InputInvalid { get { return Current.InputInvalid; } }
        public static Color HeaderBack { get { return Current.HeaderBack; } }
        // Lines
        public static Color Border { get { return Current.Border; } }
        public static Color BorderStrong { get { return Current.BorderStrong; } }
        // Text
        public static Color Text { get { return Current.Text; } }
        public static Color TextSecondary { get { return Current.TextSecondary; } }
        public static Color Muted { get { return Current.Muted; } }
        // Accent
        public static Color Accent { get { return Current.Accent; } }
        public static Color AccentHover { get { return Current.AccentHover; } }
        public static Color AccentTint { get { return Current.AccentTint; } }
        public static Color AccentSoft { get { return Current.AccentSoft; } }
        // Status
        public static Color Danger { get { return Current.Danger; } }
        public static Color Success { get { return Current.Success; } }
        public static Color SuccessText { get { return Current.SuccessText; } }
        public static Color Warning { get { return Current.Warning; } }
        public static Color Disabled { get { return Current.Disabled; } }
        public static Color RowWarning { get { return Current.RowWarning; } }
        public static Color RowDanger { get { return Current.RowDanger; } }

        public const int Radius = 6;

        public const string FontFamily = "Segoe UI";
        public const string FontFamilySemibold = "Segoe UI Semibold";

        /// <summary>Same size and style as <paramref name="source"/>, in the app typeface (bold maps to Semibold).</summary>
        public static Font Typeface(Font source)
        {
            if (source.Name == FontFamily || source.Name == FontFamilySemibold)
            {
                return (Font)source.Clone();
            }

            bool bold = (source.Style & FontStyle.Bold) != 0;
            FontStyle style = source.Style & ~FontStyle.Bold;
            return new Font(bold ? FontFamilySemibold : FontFamily, source.SizeInPoints, style, GraphicsUnit.Point);
        }

        #region Persistence of the operator's choice (per app, per Windows user)

        private static string ThemeFile
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "iFusion", "theme.txt");
            }
        }

        private static bool LoadSaved()
        {
            try
            {
                return File.Exists(ThemeFile) && File.ReadAllText(ThemeFile).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Save(bool dark)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile));
                File.WriteAllText(ThemeFile, dark ? "dark" : "light");
            }
            catch (Exception)
            {
                // the choice just won't be remembered
            }
        }

        #endregion
    }
}
