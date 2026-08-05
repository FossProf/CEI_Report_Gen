namespace CEI.ReportGenerator.Core.Services;

public static class ImagePartManager
{
    public static string GetContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => throw new InvalidOperationException(
                $"Unsupported image format: {Path.GetExtension(path)}. Supported: PNG, JPEG, GIF, BMP, TIFF.")
        };
}
