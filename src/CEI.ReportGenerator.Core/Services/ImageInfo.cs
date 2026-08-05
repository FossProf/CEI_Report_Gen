namespace CEI.ReportGenerator.Core.Services;

public static class ImageInfo
{
    public static (int Width, int Height) GetPixelSize(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        var read = 0;
        while (read < header.Length)
        {
            var n = stream.Read(header, read, header.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        if (read < 4)
        {
            throw new InvalidOperationException($"Unsupported image file: {Path.GetFileName(path)}");
        }

        if (header[0] == 0x89 && header[1] == 0x50) // PNG
        {
            if (read < 24)
            {
                throw new InvalidOperationException("Invalid PNG file.");
            }

            var w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (w, h);
        }

        if (header[0] == 0xFF && header[1] == 0xD8) // JPEG
        {
            return ReadJpegSize(stream);
        }

        if (header[0] == 'B' && header[1] == 'M') // BMP
        {
            if (read < 26)
            {
                throw new InvalidOperationException("Invalid BMP file.");
            }

            var w = BitConverter.ToInt32(header, 18);
            var h = BitConverter.ToInt32(header, 22);
            return (w, Math.Abs(h));
        }

        if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F') // GIF
        {
            if (read < 10)
            {
                throw new InvalidOperationException("Invalid GIF file.");
            }

            var w = header[6] | (header[7] << 8);
            var h = header[8] | (header[9] << 8);
            return (w, h);
        }

        throw new InvalidOperationException(
            $"Unsupported image format. Supported: PNG, JPEG, BMP, GIF. ({Path.GetFileName(path)})");
    }

    private static (int Width, int Height) ReadJpegSize(Stream stream)
    {
        stream.Position = 2;
        var marker = stream.ReadByte();
        while (marker != -1)
        {
            while (marker == 0xFF)
            {
                marker = stream.ReadByte();
            }

            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) // RST/EOI/SOI/standalone
            {
                marker = stream.ReadByte();
                continue;
            }

            var len = (stream.ReadByte() << 8) | stream.ReadByte();

            var isSof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isSof)
            {
                stream.ReadByte(); // precision
                var h = (stream.ReadByte() << 8) | stream.ReadByte();
                var w = (stream.ReadByte() << 8) | stream.ReadByte();
                return (w, h);
            }

            stream.Seek(len - 2, SeekOrigin.Current);
            marker = stream.ReadByte();
        }

        throw new InvalidOperationException("Could not determine JPEG dimensions.");
    }
}
