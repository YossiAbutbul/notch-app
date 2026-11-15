# Draws the Notch app icon at every Windows icon size and packs them into one .ico.
# Usage: powershell -NoProfile -File tools\make-icon.ps1
# Output: src\NotchApp\Assets\Notch.ico (classic 32-bit entries up to 128 px, PNG at 256 px) and Notch-256.png.
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\NotchApp\Assets"))

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class NotchIcon
{
    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath(); float d = 2 * r;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static Bitmap Draw(int S)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            // tile: a rounded square with breathing room, like Windows 11 app icons; iOS indigo into violet
            float inset = Math.Max(1f, (float)Math.Round(S * 0.055)), w = S - 2 * inset;
            using (var tile = Round(inset, inset, w, w, w * 0.235f))
            {
                using (var grad = new LinearGradientBrush(new PointF(inset, inset), new PointF(inset + w, inset + w), Color.FromArgb(0x5E, 0x5C, 0xE6), Color.FromArgb(0xB1, 0x4E, 0xF0)))
                    g.FillPath(grad, tile);
                // light from above, fading out by the middle (spans the whole tile: GDI+ gradients repeat past their ends)
                using (var sheen = new LinearGradientBrush(new PointF(0, inset - 1), new PointF(0, inset + w + 1), Color.White, Color.White))
                {
                    sheen.InterpolationColors = new ColorBlend { Positions = new[] { 0f, 0.6f, 1f },
                        Colors = new[] { Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) } };
                    g.FillPath(sheen, tile);
                }
                if (S >= 32) using (var edge = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, S / 128f))) g.DrawPath(edge, tile);
            }
            // the notch: a black pill near the top with a soft shadow under it
            float pw = w * 0.64f, ph = Math.Max(3f, w * 0.215f), px = (S - pw) / 2, py = inset + w * 0.23f;
            if (S >= 24)
                for (int k = 5; k >= 1; k--)
                {
                    float sp = S * 0.012f * k;
                    using (var sh = Round(px - sp, py - sp * 0.3f + S * 0.025f, pw + 2 * sp, ph + 2 * sp, (ph + 2 * sp) / 2))
                    using (var b = new SolidBrush(Color.FromArgb(16, 25, 0, 70))) g.FillPath(b, sh);
                }
            using (var pill = Round(px, py, pw, ph, ph / 2))
            using (var b = new SolidBrush(Color.FromArgb(6, 6, 9))) g.FillPath(b, pill);
            // inside it: album art at the leading end (when there's room), the timer's orange dot at the trailing end
            float dot = ph * 0.44f;
            using (var b = new SolidBrush(Color.FromArgb(0xFF, 0x9F, 0x0A)))
                g.FillEllipse(b, px + pw - ph / 2 - dot / 2, py + (ph - dot) / 2, dot, dot);
            if (S >= 40)
            {
                float a = ph * 0.52f;
                using (var art = Round(px + ph / 2 - a / 2, py + (ph - a) / 2, a, a, a * 0.28f))
                using (var b = new LinearGradientBrush(new PointF(0, py), new PointF(0, py + ph), Color.FromArgb(0xFF, 0x6B, 0x8A), Color.FromArgb(0xFF, 0x37, 0x5F)))
                    g.FillPath(b, art);
            }
        }
        return bmp;
    }

    /// <summary>32-bit DIB entry (what every Windows API reads), or PNG for 256 px.</summary>
    static byte[] Entry(Bitmap b)
    {
        int s = b.Width;
        if (s >= 256) { using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)               // bottom-up BGRA
                for (int x = 0; x < s; x++) { var c = b.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
            int maskRow = ((s + 31) / 32) * 4;             // AND mask: all zero (alpha does the work)
            w.Write(new byte[maskRow * s]);
            return ms.ToArray();
        }
    }

    public static void Write(string dir, int[] sizes)
    {
        var data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var b = Draw(sizes[i]))
            {
                data[i] = Entry(b);
                if (sizes[i] == 256) b.Save(Path.Combine(dir, "Notch-256.png"), ImageFormat.Png);
            }
        using (var fs = File.Create(Path.Combine(dir, "Notch.ico")))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(data[i].Length); w.Write(offset);
                offset += data[i].Length;
            }
            foreach (var d in data) w.Write(d);
        }
    }
}
'@

New-Item -ItemType Directory -Force $OutDir | Out-Null
$full = (Resolve-Path $OutDir).Path
[NotchIcon]::Write($full, [int[]](16, 20, 24, 32, 40, 48, 64, 96, 128, 256))
"Wrote $full\Notch.ico"
