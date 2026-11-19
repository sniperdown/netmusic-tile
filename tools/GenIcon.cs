// Generates app.ico: a purple rounded tile with a white music note.
// No source image needed, and no external tool (no ImageMagick on this machine).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class GenIcon
{
    static void Main(string[] args)
    {
        // Output path: first argument, else app/app.ico relative to the current
        // directory. No absolute path is baked in, so this works from any checkout.
        string outPath = args.Length > 0 ? args[0] : Path.Combine("app", "app.ico");
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

        var pngs = new System.Collections.Generic.List<byte[]>();
        foreach (int s in sizes)
            pngs.Add(Render(s));

        using (var fs = new FileStream(outPath, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);              // reserved
            w.Write((ushort)1);              // type = icon
            w.Write((ushort)sizes.Length);   // count

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));   // width  (0 means 256)
                w.Write((byte)(s >= 256 ? 0 : s));   // height
                w.Write((byte)0);                    // palette
                w.Write((byte)0);                    // reserved
                w.Write((ushort)1);                  // planes
                w.Write((ushort)32);                 // bpp
                w.Write((uint)pngs[i].Length);
                w.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length + " bytes)");
    }

    static byte[] Render(int size)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                float r = size * 0.22f;
                var rect = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);

                using (var path = RoundedRect(rect, r))
                using (var grad = new LinearGradientBrush(rect, Color.FromArgb(255, 122, 92, 255),
                                                                Color.FromArgb(255, 74, 48, 190), 60f))
                    g.FillPath(grad, path);

                // music note: a filled head + a stem + a flag
                float cx = size * 0.42f, cy = size * 0.66f;
                float headR = size * 0.13f;
                using (var brush = new SolidBrush(Color.White))
                {
                    g.FillEllipse(brush, cx - headR, cy - headR * 0.78f, headR * 2, headR * 1.56f);

                    float stemW = Math.Max(1f, size * 0.055f);
                    g.FillRectangle(brush, cx + headR - stemW * 0.5f, size * 0.24f,
                                    stemW, cy - size * 0.24f - headR * 0.2f);

                    // flag
                    var flag = new PointF[]
                    {
                        new PointF(cx + headR - stemW * 0.5f, size * 0.24f),
                        new PointF(size * 0.74f,             size * 0.32f),
                        new PointF(size * 0.74f,             size * 0.46f),
                        new PointF(cx + headR - stemW * 0.5f, size * 0.40f),
                    };
                    g.FillPolygon(brush, flag);
                }
            }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
