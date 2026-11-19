# Draws the Notch app icon at every Windows icon size and packs them into one .ico.
# Usage: powershell -NoProfile -File tools\make-icon.ps1
# Output: src\NotchApp\Assets\Notch.ico (classic 32-bit entries up to 128 px, PNG at 256 px) and Notch-256.png.
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\NotchApp\Assets"))

# (C# 5: this runs on Windows PowerShell's built-in compiler, so no newer syntax such as => members)
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class NotchIcon
{
    /// <summary>A squircle: continuous curvature all the way round (a superellipse), the shape of today's app icons.</summary>
    static GraphicsPath Squircle(float x, float y, float w, float h)
    {
        const double n = 5.0;
        var p = new GraphicsPath(); int steps = 360; var pts = new PointF[steps];
        float cx = x + w / 2, cy = y + h / 2, a = w / 2, b = h / 2;
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps, c = Math.Cos(t), s = Math.Sin(t);
            pts[i] = new PointF(cx + (float)(a * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / n)), cy + (float)(b * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / n)));
        }
        p.AddPolygon(pts); return p;
    }

    static GraphicsPath Capsule(float x, float y, float w, float h)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, h, h, 90, 180); p.AddArc(x + w - h, y, h, h, 270, 180); p.CloseFigure(); return p;
    }

    /// <summary>
    /// The icon, "island on colour": a squircle in iOS indigo running into violet, lit from above, and on it the notch,
    /// a black capsule near the top with its camera-lens dot, casting a soft shadow. Drawn on a 1024 px canvas and
    /// scaled down smoothly. Small sizes (24 px and below) lose the dot and the shadow and get a bolder capsule, so
    /// they stay crisp in the tray and title bar.
    /// </summary>
    static void Paint(Graphics g, float S, bool small)
    {
        float i = S * (small ? 0.03f : 0.06f), w = S - 2 * i;
        using (var tile = Squircle(i, i, w, w))
        {
            using (var b = new LinearGradientBrush(new PointF(i, i), new PointF(i + w, i + w), Color.FromArgb(0x5E, 0x5C, 0xE6), Color.FromArgb(0xBF, 0x5A, 0xF2)))
                g.FillPath(b, tile);
            // light from above, gone by just past the middle
            using (var b = new LinearGradientBrush(new PointF(0, i - 1), new PointF(0, i + w + 1), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)))
            {
                b.Blend = new Blend { Positions = new[] { 0f, 0.55f, 1f }, Factors = new[] { 0f, 1f, 1f } };
                g.FillPath(b, tile);
            }
        }
        float pw = w * (small ? 0.66f : 0.56f), ph = w * (small ? 0.26f : 0.19f), px = (S - pw) / 2, py = i + w * (small ? 0.28f : 0.3f);
        if (!small)
            for (int k = 8; k >= 1; k--)
            {
                float s = S * 0.006f * k;
                using (var sh = Capsule(px - s, py - s * 0.2f + S * 0.03f, pw + 2 * s, ph + 2 * s))
                using (var b = new SolidBrush(Color.FromArgb(10, 20, 0, 60))) g.FillPath(b, sh);
            }
        using (var pill = Capsule(px, py, pw, ph))
        using (var b = new LinearGradientBrush(new PointF(0, py - 1), new PointF(0, py + ph + 1), Color.FromArgb(28, 28, 32), Color.FromArgb(2, 2, 4))) g.FillPath(b, pill);
        if (!small)
        {
            float d = ph * 0.34f;
            using (var b = new SolidBrush(Color.FromArgb(0x26, 0x2A, 0x4A))) g.FillEllipse(b, px + pw - ph * 0.5f - d / 2, py + (ph - d) / 2, d, d);
        }
    }

    public static Bitmap Draw(int S)
    {
        const int big = 1024;
        using (var hi = new Bitmap(big, big, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(hi))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
                Paint(g, big, S <= 24);
            }
            var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var attr = new ImageAttributes())
                {
                    attr.SetWrapMode(WrapMode.TileFlipXY);   // no grey fringe from sampling past the edge
                    g.DrawImage(hi, new Rectangle(0, 0, S, S), 0, 0, big, big, GraphicsUnit.Pixel, attr);
                }
            }
            return bmp;
        }
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
