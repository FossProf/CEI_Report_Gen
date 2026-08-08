using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.ViewModels;

public sealed class PhotoItem : ObservableObject
{
    private Photo _photo;
    private string _caption = string.Empty;
    private ImageSource? _thumbnail;
    private int _number;

    public PhotoItem(Photo photo)
    {
        _photo = photo;
        _caption = photo.Caption;
        RefreshThumbnail();
    }

    public Photo Model => _photo;

    public string SourcePath => _photo.SourcePath;

    public string FileName => string.IsNullOrWhiteSpace(_photo.SourcePath)
        ? "(missing)"
        : Path.GetFileName(_photo.SourcePath);

    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
            {
                OnPropertyChanged(nameof(NumberLabel));
            }
        }
    }

    public string NumberLabel => $"Photo {_number}:";

    public string Caption
    {
        get => _caption;
        set
        {
            if (SetProperty(ref _caption, value))
            {
                _photo.Caption = value;
            }
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public void UpdateSource(string path)
    {
        _photo.SourcePath = path;
        _photo.StoredFileName = string.Empty;
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(FileName));
        RefreshThumbnail();
    }

    private void RefreshThumbnail()
    {
        Thumbnail = null;
        if (string.IsNullOrWhiteSpace(_photo.SourcePath) || !File.Exists(_photo.SourcePath))
        {
            return;
        }

        try
        {
            var bytes = ImageNormalizer.GetNormalizedBytes(_photo.SourcePath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            Thumbnail = bitmap;
        }
        catch
        {
            Thumbnail = null;
        }
    }
}
