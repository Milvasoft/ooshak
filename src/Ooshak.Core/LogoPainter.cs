using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ooshak.Core;

/// <summary>Draws the Ooshak mark: sound bars that form a bow tie. Small sizes use fewer, wider bars.</summary>
public static class LogoPainter
{
    private const float Grid = 256f;
    private const float CornerRadius = 56f;
    private const int DetailedMinSize = 40;

    private static readonly (float X, float Width, float Height, bool Knot)[] Detailed =
    [
        (28, 20, 164, false), (58, 20, 112, false), (88, 20, 56, false), (118, 20, 88, true),
        (148, 20, 56, false), (178, 20, 112, false), (208, 20, 164, false),
    ];

    private static readonly (float X, float Width, float Height, bool Knot)[] Simple =
    [
        (22, 36, 176, false), (70, 36, 92, false), (110, 36, 124, true), (150, 36, 92, false), (198, 36, 176, false),
    ];

    public static Bitmap Render(int size, Color wings, Color knot)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.ScaleTransform(size / Grid, size / Grid);

        using (var tile = Rounded(0, 0, Grid, Grid, CornerRadius))
        using (var brush = new SolidBrush(Color.FromArgb(0x17, 0x13, 0x0E)))
            graphics.FillPath(brush, tile);

        using var wingBrush = new SolidBrush(wings);
        using var knotBrush = new SolidBrush(knot);

        foreach (var (x, width, height, isKnot) in size >= DetailedMinSize ? Detailed : Simple)
        {
            using var bar = Rounded(x, (Grid - height) / 2, width, height, width / 2);
            graphics.FillPath(isKnot ? knotBrush : wingBrush, bar);
        }

        return bitmap;
    }

    /// <summary>Writes a multi-resolution .ico whose images are stored as PNG.</summary>
    public static void WriteIcon(string path, IReadOnlyList<int> sizes, Color wings, Color knot)
    {
        var images = sizes.Select(size =>
        {
            using var bitmap = Render(size, wings, knot);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return (Size: size, Data: stream.ToArray());
        }).ToList();

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Count);

        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
            writer.Write(data);
    }

    private static GraphicsPath Rounded(float x, float y, float width, float height, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
