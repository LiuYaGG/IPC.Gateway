using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace IPC.Gateway.WebHost;

/// <summary>Small cross-platform RGBA renderer and PNG encoder; no native graphics or SVG metadata.</summary>
internal static class GatewayLoginCaptchaImage
{
    private const int Width = GatewayLoginCaptchaService.ImageWidth;
    private const int Height = GatewayLoginCaptchaService.ImageHeight;
    private const int Size = GatewayLoginCaptchaService.PieceSize;
    private static readonly uint[] CrcTable = CreateCrcTable();
    private const int SceneCount = 6;
    private static int _lastScene = -1;

    public static (string Background, string Piece) Create(int targetX, int pieceY)
    {
        var random = new Random(RandomNumberGenerator.GetInt32(int.MaxValue));
        byte[] background = DrawLandscape(random);
        byte[] piece = new byte[Size * Size * 4];
        bool flipX = random.Next(2) == 1;
        bool flipY = random.Next(2) == 1;
        bool[] mask = new bool[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            mask[y * Size + x] = IsInside(flipX ? Size - 1 - x : x, flipY ? Size - 1 - y : y);

        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int source = ((pieceY + y) * Width + targetX + x) * 4;
            int destination = (y * Size + x) * 4;
            if (!mask[y * Size + x]) continue;
            bool edge = x == 0 || y == 0 || x == Size - 1 || y == Size - 1 ||
                !mask[y * Size + x - 1] || !mask[y * Size + x + 1] ||
                !mask[(y - 1) * Size + x] || !mask[(y + 1) * Size + x];
            Array.Copy(background, source, piece, destination, 4);
            if (edge)
            {
                for (int channel = 0; channel < 3; channel++)
                    piece[destination + channel] = (byte)((piece[destination + channel] + 255) / 2);
            }
            // Replace the cutout with an opaque shaded recess. The original image pixels
            // are removed, while uniform background alpha avoids encoding the answer in an alpha mask.
            background[source] = (byte)random.Next(18, 30);
            background[source + 1] = (byte)random.Next(38, 53);
            background[source + 2] = (byte)random.Next(67, 85);
            background[source + 3] = 255;
        }

        return (ToDataUrl(background, Width, Height), ToDataUrl(piece, Size, Size));
    }

    private static bool IsInside(int x, int y)
    {
        bool rectangle = x >= 7 && x <= 39 && y >= 7 && y <= 39;
        bool topTab = DistanceSquared(x, y, 23, 7) <= 49;
        bool rightTab = DistanceSquared(x, y, 39, 23) <= 49;
        bool bottomSocket = DistanceSquared(x, y, 23, 39) <= 49;
        bool leftSocket = DistanceSquared(x, y, 7, 23) <= 49;
        return (rectangle || topTab || rightTab) && !bottomSocket && !leftSocket;
    }

    private static int DistanceSquared(int x, int y, int cx, int cy) => (x - cx) * (x - cx) + (y - cy) * (y - cy);

    private static byte[] DrawLandscape(Random random)
    {
        byte[] pixels = new byte[Width * Height * 4];
        // Each request changes the scene as well as its geometry and palette. All
        // rendering stays in memory; there are no image files, downloads or native codecs.
        double phase = random.NextDouble() * Math.Tau;
        int sunX = random.Next(35, Width - 35);
        switch (NextScene(random))
        {
            case 0: // Alpine lake.
                FillGradient(pixels, new(99, 173, 217), new(222, 239, 240));
                FillDisc(pixels, sunX, random.Next(22, 39), random.Next(10, 15), new(255, 240, 190));
                DrawClouds(pixels, random, new(241, 250, 251));
                FillRidge(pixels, 70, 20, .026, phase, new(122, 160, 183));
                FillRidge(pixels, 94, 17, .032, phase + 2, new(55, 113, 138));
                FillWater(pixels, random.Next(113, 125), new(71, 153, 184), new(33, 106, 148), phase);
                break;
            case 1: // Warm dunes.
                FillGradient(pixels, new(94, 169, 196), new(245, 226, 188));
                FillDisc(pixels, sunX, random.Next(25, 44), random.Next(12, 17), new(255, 224, 159));
                DrawClouds(pixels, random, new(249, 241, 221));
                FillRidge(pixels, 88, 13, .013, phase, new(227, 188, 131));
                FillRidge(pixels, 110, 17, .017, phase + 2, new(203, 144, 93));
                FillRidge(pixels, 139, 14, .014, phase + 4, new(157, 100, 72));
                DrawCactus(pixels, random.Next(27, 82), random.Next(123, 143), random.Next(27, 41));
                DrawCactus(pixels, random.Next(227, 293), random.Next(131, 149), random.Next(20, 34));
                break;
            case 2: // Pine forest.
                FillGradient(pixels, new(149, 204, 190), new(231, 237, 198));
                FillDisc(pixels, sunX, random.Next(22, 39), random.Next(11, 16), new(249, 240, 195));
                DrawClouds(pixels, random, new(231, 243, 224));
                FillRidge(pixels, 78, 12, .022, phase, new(111, 164, 142));
                FillRidge(pixels, 116, 12, .019, phase + 2, new(66, 124, 104));
                for (int x = -10; x < Width + 20; x += random.Next(20, 34))
                    DrawPine(pixels, x, random.Next(143, 169), random.Next(55, 94), new(34, 89, 78));
                break;
            case 3: // Turquoise islands.
                FillGradient(pixels, new(89, 181, 224), new(222, 244, 242));
                FillDisc(pixels, sunX, random.Next(21, 35), random.Next(10, 15), new(255, 244, 201));
                DrawClouds(pixels, random, new(241, 251, 251));
                FillWater(pixels, random.Next(78, 91), new(79, 191, 194), new(32, 139, 164), phase);
                int islandX = random.Next(58, 118);
                FillEllipse(pixels, islandX, 124, random.Next(43, 58), 12, new(237, 219, 162));
                FillEllipse(pixels, islandX + 4, 118, 39, 9, new(75, 150, 110));
                DrawPalm(pixels, islandX + 8, 119, random.Next(32, 46), random);
                FillEllipse(pixels, random.Next(236, 275), 99, 27, 6, new(91, 165, 137));
                break;
            case 4: // Violet sunset.
                FillGradient(pixels, new(90, 78, 139), new(248, 175, 137));
                FillDisc(pixels, sunX, random.Next(38, 60), random.Next(16, 22), new(255, 215, 164));
                DrawClouds(pixels, random, new(224, 174, 187));
                FillRidge(pixels, 91, 19, .019, phase, new(135, 111, 159));
                FillRidge(pixels, 112, 16, .025, phase + 3, new(82, 81, 129));
                FillWater(pixels, 130, new(166, 134, 173), new(105, 100, 152), phase);
                break;
            default: // Rolling fields and a winding path.
                FillGradient(pixels, new(156, 204, 227), new(238, 241, 210));
                FillDisc(pixels, sunX, random.Next(21, 37), random.Next(10, 15), new(255, 241, 193));
                DrawClouds(pixels, random, new(246, 250, 236));
                FillRidge(pixels, 80, 13, .013, phase, new(140, 179, 146));
                FillRidge(pixels, 105, 16, .016, phase + 2, new(100, 154, 111));
                FillRidge(pixels, 133, 13, .019, phase + 4, new(167, 183, 114));
                DrawPath(pixels, random.Next(120, 201), phase);
                break;
        }

        int tint = random.Next(-7, 8);
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            int offset = (y * Width + x) * 4;
            int noise = random.Next(-3, 4) + tint;
            for (int channel = 0; channel < 3; channel++)
                pixels[offset + channel] = (byte)Math.Clamp(pixels[offset + channel] + noise, 0, 255);
        }
        return pixels;
    }

    private static int NextScene(Random random)
    {
        int previous;
        int selected;
        do
        {
            previous = Volatile.Read(ref _lastScene);
            selected = random.Next(SceneCount - (previous < 0 ? 0 : 1));
            if (previous >= 0 && selected >= previous) selected++;
        } while (Interlocked.CompareExchange(ref _lastScene, selected, previous) != previous);
        return selected;
    }

    private readonly record struct Rgb(int R, int G, int B);

    private static Rgb Mix(Rgb from, Rgb to, double amount) => new(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private static void Paint(byte[] pixels, int x, int y, Rgb color)
    {
        int offset = (y * Width + x) * 4;
        pixels[offset] = (byte)Math.Clamp(color.R, 0, 255);
        pixels[offset + 1] = (byte)Math.Clamp(color.G, 0, 255);
        pixels[offset + 2] = (byte)Math.Clamp(color.B, 0, 255);
        pixels[offset + 3] = 255;
    }

    private static void FillGradient(byte[] pixels, Rgb top, Rgb bottom)
    {
        for (int y = 0; y < Height; y++)
        {
            Rgb color = Mix(top, bottom, (double)y / (Height - 1));
            for (int x = 0; x < Width; x++) Paint(pixels, x, y, color);
        }
    }

    private static void FillEllipse(byte[] pixels, int cx, int cy, int rx, int ry, Rgb color)
    {
        for (int y = Math.Max(0, cy - ry); y <= Math.Min(Height - 1, cy + ry); y++)
        {
            double dy = (double)(y - cy) / ry;
            int halfWidth = (int)(rx * Math.Sqrt(Math.Max(0, 1 - dy * dy)));
            for (int x = Math.Max(0, cx - halfWidth); x <= Math.Min(Width - 1, cx + halfWidth); x++)
                Paint(pixels, x, y, color);
        }
    }

    private static void FillDisc(byte[] pixels, int x, int y, int radius, Rgb color) =>
        FillEllipse(pixels, x, y, radius, radius, color);

    private static void DrawClouds(byte[] pixels, Random random, Rgb color)
    {
        for (int cloud = 0; cloud < 3; cloud++)
        {
            int x = random.Next(15, Width - 15), y = random.Next(17, 54), rx = random.Next(12, 25);
            FillEllipse(pixels, x, y, rx, 4, color);
            FillEllipse(pixels, x + rx / 3, y - 4, rx / 2, 7, color);
        }
    }

    private static void FillRidge(byte[] pixels, int baseline, int amplitude, double frequency, double phase, Rgb color)
    {
        for (int x = 0; x < Width; x++)
        {
            int top = (int)(baseline + amplitude * Math.Sin(x * frequency + phase)
                + amplitude * .25 * Math.Cos(x * frequency * 2.3 + phase));
            for (int y = Math.Max(0, top); y < Height; y++)
                Paint(pixels, x, y, Mix(color, new(color.R * 4 / 5, color.G * 4 / 5, color.B * 4 / 5), (y - top) / 120.0));
        }
    }

    private static void FillWater(byte[] pixels, int horizon, Rgb top, Rgb bottom, double phase)
    {
        for (int y = horizon; y < Height; y++)
        {
            Rgb color = Mix(top, bottom, (double)(y - horizon) / (Height - horizon));
            for (int x = 0; x < Width; x++)
            {
                int ripple = (int)(Math.Sin(x * .11 + y * 1.4 + phase) * 4);
                Paint(pixels, x, y, new(color.R + ripple, color.G + ripple, color.B + ripple));
            }
        }
    }

    private static void FillRectangle(byte[] pixels, int left, int top, int width, int height, Rgb color)
    {
        for (int y = Math.Max(0, top); y < Math.Min(Height, top + height); y++)
        for (int x = Math.Max(0, left); x < Math.Min(Width, left + width); x++)
            Paint(pixels, x, y, color);
    }

    private static void DrawPine(byte[] pixels, int cx, int bottom, int height, Rgb color)
    {
        FillRectangle(pixels, cx - 2, bottom - height, 4, height, new(61, 92, 76));
        for (int layer = 0; layer < 3; layer++)
        {
            int top = bottom - height + layer * height / 5;
            int layerHeight = height / 2;
            for (int y = Math.Max(0, top); y < Math.Min(Height, top + layerHeight); y++)
            {
                int halfWidth = (y - top) * (layer + 4) / 8;
                for (int x = Math.Max(0, cx - halfWidth); x <= Math.Min(Width - 1, cx + halfWidth); x++)
                    Paint(pixels, x, y, x < cx ? color : new(color.R * 4 / 5, color.G * 4 / 5, color.B * 4 / 5));
            }
        }
    }

    private static void DrawCactus(byte[] pixels, int x, int bottom, int height)
    {
        Rgb color = new(67, 107, 92);
        FillRectangle(pixels, x - 3, bottom - height, 7, height, color);
        FillDisc(pixels, x, bottom - height, 3, color);
        FillRectangle(pixels, x - 12, bottom - height / 2, 12, 5, color);
        FillRectangle(pixels, x - 12, bottom - height * 3 / 4, 5, height / 4, color);
        FillDisc(pixels, x - 10, bottom - height * 3 / 4, 2, color);
        FillRectangle(pixels, x + 2, bottom - height * 2 / 3, 11, 5, color);
        FillRectangle(pixels, x + 9, bottom - height * 4 / 5, 5, height / 4, color);
        FillDisc(pixels, x + 11, bottom - height * 4 / 5, 2, color);
    }

    private static void DrawPalm(byte[] pixels, int x, int bottom, int height, Random random)
    {
        Rgb trunk = new(115, 107, 76), leaves = new(39, 114, 90);
        for (int y = bottom - height; y < bottom; y++)
            FillRectangle(pixels, x + (y - bottom) / 10, y, 3, 1, trunk);
        int cx = x - height / 10, cy = bottom - height;
        for (int leaf = 0; leaf < 6; leaf++)
        {
            double angle = leaf * Math.PI / 4 + .5 + random.NextDouble() * .2;
            int length = random.Next(16, 25);
            for (int step = 0; step < length; step++)
                FillDisc(pixels, cx + (int)(Math.Cos(angle) * step), cy + (int)(Math.Sin(angle) * step * .45 + step * step * .015), 1, leaves);
        }
    }

    private static void DrawPath(byte[] pixels, int center, double phase)
    {
        for (int y = 102; y < Height; y++)
        {
            int cx = center + (int)(Math.Sin(y * .043 + phase) * (y - 94) * .48);
            int halfWidth = (y - 100) / 6 + 1;
            FillRectangle(pixels, cx - halfWidth, y, halfWidth * 2, 1, new(226, 214, 169));
        }
    }

    private static string ToDataUrl(byte[] pixels, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6; // 8-bit RGBA.
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            int stride = width * 4;
            byte[] row = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(1); // Sub filter improves noisy RGB gradients without native codecs.
                pixels.AsSpan(y * stride, stride).CopyTo(row);
                for (int i = stride - 1; i >= 4; i--) row[i] = unchecked((byte)(row[i] - row[i - 4]));
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        byte[] name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte value in name) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        foreach (byte value in data) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320U ^ (crc >> 1) : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
