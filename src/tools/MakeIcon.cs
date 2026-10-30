using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class MakeIcon
{
    static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
    const int MaxWorkSize = 4096;

    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: MakeIcon <picture.png> <output.ico>");
            return 2;
        }
        try
        {
            int srcW, srcH;
            byte[][] frames = new byte[Sizes.Length][];
            using (Bitmap square = LoadSquare(args[0], out srcW, out srcH))
            {
                for (int i = 0; i < Sizes.Length; i++)
                {
                    using (Bitmap b = Resize(square, Sizes[i]))
                        frames[i] = Sizes[i] >= 256 ? EncodePng(b) : EncodeDib(b);
                }
            }
            WriteIco(args[1], frames);
            Console.WriteLine("  Icon: " + Path.GetFileName(args[0]) + " (" + srcW + " x " + srcH + " px)");
            if (srcW != srcH) Console.WriteLine("        not square - centered on a transparent background");
            if (Math.Max(srcW, srcH) < 256) Console.WriteLine("        tip: a 256 x 256 px (or larger) picture gives the sharpest icon");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("  [!] Could not use " + args[0] + " as an icon: " + ex.Message);
            return 1;
        }
    }

    static Bitmap LoadSquare(string path, out int w, out int h)
    {
        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (Image img = Image.FromStream(fs, true, true))
        {
            w = img.Width;
            h = img.Height;
            int side = Math.Max(w, h);
            int target = Math.Min(side, MaxWorkSize);
            double scale = (double)target / side;
            int dw = Math.Max(1, (int)Math.Round(w * scale));
            int dh = Math.Max(1, (int)Math.Round(h * scale));
            Bitmap sq = new Bitmap(target, target, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(sq))
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                g.DrawImage(img, new Rectangle((target - dw) / 2, (target - dh) / 2, dw, dh), 0, 0, w, h, GraphicsUnit.Pixel, ia);
            }
            return sq;
        }
    }

    static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap cur = src;
        while (cur.Width > size * 2)
        {
            Bitmap half = Scale(cur, cur.Width / 2);
            if (cur != src) cur.Dispose();
            cur = half;
        }
        Bitmap result = Scale(cur, size);
        if (cur != src) cur.Dispose();
        return result;
    }

    static Bitmap Scale(Bitmap src, int size)
    {
        Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(dst))
        using (ImageAttributes ia = new ImageAttributes())
        {
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return dst;
    }

    static byte[] EncodePng(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    static byte[] EncodeDib(Bitmap b)
    {
        int n = b.Width;
        byte[] px = new byte[n * n * 4];
        BitmapData data = b.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < n; y++)
                Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), px, y * n * 4, n * 4);
        }
        finally { b.UnlockBits(data); }

        int maskStride = ((n + 31) / 32) * 4;
        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter w = new BinaryWriter(ms))
        {
            w.Write(40);
            w.Write(n);
            w.Write(n * 2);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);
            w.Write(n * n * 4 + maskStride * n);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--) w.Write(px, y * n * 4, n * 4);
            byte[] row = new byte[maskStride];
            for (int y = n - 1; y >= 0; y--)
            {
                Array.Clear(row, 0, row.Length);
                for (int x = 0; x < n; x++)
                    if (px[(y * n + x) * 4 + 3] == 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                w.Write(row);
            }
            w.Flush();
            return ms.ToArray();
        }
    }

    static void WriteIco(string path, byte[][] frames)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);
            w.Write((short)1);
            w.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                int s = Sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(frames[i].Length);
                w.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) w.Write(f);
        }
    }
}
