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
            var (w, h, orientation) = ReadJpegInfo(stream);
            if (orientation is 5 or 6 or 7 or 8)
            {
                (w, h) = (h, w);
            }

            return (w, h);
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

    private static (int Width, int Height, int Orientation) ReadJpegInfo(Stream stream)
    {
        stream.Position = 2;
        var marker = stream.ReadByte();
        var orientation = 1;
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
            var payloadStart = stream.Position;

            if (marker == 0xE1) // APP1 - Exif
            {
                var payload = new byte[len - 2];
                var read = ReadFully(stream, payload);
                if (read >= 14 && payload[0] == 'E' && payload[1] == 'x' && payload[2] == 'i' && payload[3] == 'f'
                    && payload[4] == 0 && payload[5] == 0)
                {
                    var exifOrientation = TryReadExifOrientation(payload, 6);
                    if (exifOrientation >= 1 && exifOrientation <= 8)
                    {
                        orientation = exifOrientation;
                    }
                }

                if (read < len - 2)
                {
                    stream.Position = payloadStart + len - 2;
                }
            }
            else
            {
                var isSof = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
                if (isSof)
                {
                    stream.ReadByte(); // precision
                    var h = (stream.ReadByte() << 8) | stream.ReadByte();
                    var w = (stream.ReadByte() << 8) | stream.ReadByte();
                    return (w, h, orientation);
                }

                stream.Seek(len - 2, SeekOrigin.Current);
            }

            marker = stream.ReadByte();
        }

        throw new InvalidOperationException("Could not determine JPEG dimensions.");
    }

    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    private static int TryReadExifOrientation(byte[] data, int tiffStart)
    {
        bool littleEndian;
        if (data[tiffStart] == 0x49 && data[tiffStart + 1] == 0x49)
        {
            littleEndian = true;
        }
        else if (data[tiffStart] == 0x4D && data[tiffStart + 1] == 0x4D)
        {
            littleEndian = false;
        }
        else
        {
            return 0;
        }

        var ifd0Offset = ReadInt32(data, tiffStart + 4, littleEndian);
        var countOffset = tiffStart + ifd0Offset;
        if (ifd0Offset < 8 || countOffset + 2 > data.Length)
        {
            return 0;
        }

        var count = ReadUInt16(data, countOffset, littleEndian);
        for (var i = 0; i < count; i++)
        {
            var entry = countOffset + 2 + i * 12;
            if (entry + 12 > data.Length)
            {
                break;
            }

            if (ReadUInt16(data, entry, littleEndian) != 0x0112)
            {
                continue;
            }

            var type = ReadUInt16(data, entry + 2, littleEndian);
            return type switch
            {
                3 => ReadUInt16(data, entry + 8, littleEndian), // SHORT
                4 => ReadInt32(data, entry + 8, littleEndian),  // LONG
                _ => 0,
            };
        }

        return 0;
    }

    private static ushort ReadUInt16(byte[] data, int offset, bool littleEndian)
    {
        return littleEndian
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static int ReadInt32(byte[] data, int offset, bool littleEndian)
    {
        return littleEndian
            ? data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24)
            : (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
