using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrackSaveEditor.Core;

/// <summary>
/// Builds a looping animated GIF. WPF's GifBitmapEncoder writes neither frame delays nor the loop
/// extension, so every frame is encoded on its own and the image blocks are stitched together,
/// moving each frame's global colour table into a local one.
/// </summary>
public static class GifWriter
{
    public static void Write(string path, IReadOnlyList<BitmapSource> frames, int delayMs)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames.");
        int w = frames[0].PixelWidth, h = frames[0].PixelHeight;
        ushort delay = (ushort)Math.Max(2, delayMs / 10);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("GIF89a"u8);
        bw.Write((ushort)w);
        bw.Write((ushort)h);
        bw.Write((byte)0x70); // no global colour table, 8-bit colour resolution
        bw.Write((byte)0);
        bw.Write((byte)0);

        // NETSCAPE2.0 application extension: loop forever
        bw.Write([0x21, 0xFF, 0x0B]);
        bw.Write("NETSCAPE2.0"u8);
        bw.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        foreach (var frame in frames)
        {
            var enc = new GifBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            enc.Save(ms);
            byte[] g = ms.ToArray();

            byte packed = g[10];
            bool hasGct = (packed & 0x80) != 0;
            int gctSizeBits = packed & 0x07;
            int gctLen = hasGct ? 3 * (1 << (gctSizeBits + 1)) : 0;
            int p = 13;
            byte[] gct = g.AsSpan(p, gctLen).ToArray();
            p += gctLen;

            // Skip extensions until the image descriptor.
            while (p < g.Length && g[p] != 0x2C)
            {
                if (g[p] != 0x21) throw new InvalidDataException("Unexpected GIF layout.");
                p += 2;
                while (g[p] != 0) p += g[p] + 1;
                p++;
            }

            // Graphic control extension with the frame delay.
            bw.Write([0x21, 0xF9, 0x04, 0x04]);
            bw.Write(delay);
            bw.Write([0x00, 0x00]);

            // Image descriptor: copy position/size, switch on local colour table.
            bw.Write(g, p, 9);
            byte localPacked = g[p + 9];
            p += 10;
            if ((localPacked & 0x80) != 0)
            {
                int lctLen = 3 * (1 << ((localPacked & 0x07) + 1));
                bw.Write(localPacked);
                bw.Write(g, p, lctLen);
                p += lctLen;
            }
            else
            {
                bw.Write(hasGct ? (byte)(0x80 | (localPacked & 0x40) | gctSizeBits) : localPacked);
                bw.Write(gct);
            }

            // LZW minimum code size + data sub-blocks up to (and including) the block terminator.
            int start = p;
            p++;
            while (g[p] != 0) p += g[p] + 1;
            p++;
            bw.Write(g, start, p - start);
        }
        bw.Write((byte)0x3B);
    }

    public static BitmapSource LoadScaled(string file, int width)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = width;
        img.UriSource = new Uri(file);
        img.EndInit();
        var converted = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }
}
