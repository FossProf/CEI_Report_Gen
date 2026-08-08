using System.Drawing;
using System.Drawing.Imaging;

namespace CEI.ReportGenerator.Core.Services;

public static class ImageNormalizer
{
    public static byte[] GetNormalizedBytes(string path)
    {
        var orientation = ImageInfo.GetExifOrientation(path);
        if (orientation <= 1)
        {
            return File.ReadAllBytes(path);
        }

        return RotateAndEncode(path, orientation);
    }

    private static byte[] RotateAndEncode(string path, int orientation)
    {
        try
        {
            using var bitmap = new Bitmap(path);
            bitmap.RotateFlip(MapToRotateFlipType(orientation));
            if (bitmap.PropertyIdList.Contains(0x0112))
            {
                bitmap.RemovePropertyItem(0x0112);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            return stream.ToArray();
        }
        catch
        {
            return File.ReadAllBytes(path);
        }
    }

    private static RotateFlipType MapToRotateFlipType(int orientation)
    {
        return orientation switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.RotateNoneFlipY,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate90FlipY,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
    }
}
