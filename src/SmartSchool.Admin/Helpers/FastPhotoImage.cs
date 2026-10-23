using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SmartSchool.Admin.Helpers;

/// <summary>Non-blocking, downsampled, memory+disk-cached photo image for student rows.</summary>
public sealed class FastPhotoImage : Image
{
    public static readonly DependencyProperty UriSourceProperty = DependencyProperty.Register(
        nameof(UriSource), typeof(string), typeof(FastPhotoImage),
        new PropertyMetadata(null, OnUriSourceChanged));

    public string? UriSource
    {
        get => (string?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    private long _requestId;

    private static async void OnUriSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FastPhotoImage image) return;
        var url = e.NewValue as string;
        var requestId = Interlocked.Increment(ref image._requestId);
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        var source = await StudentPhotoCache.GetAsync(url);
        if (source is null || requestId != Interlocked.Read(ref image._requestId)) return;
        if (image.Dispatcher.CheckAccess()) image.Source = source;
        else await image.Dispatcher.InvokeAsync(() => image.Source = source);
    }
}

internal static class StudentPhotoCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource?>>> Memory = new();
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QueenInternationalSchool", "PhotoCache");

    public static async Task<BitmapSource?> GetAsync(string url)
    {
        var task = Memory.GetOrAdd(url, key => new Lazy<Task<BitmapSource?>>(() => LoadAsync(key))).Value;
        var image = await task.ConfigureAwait(false);
        if (image is null) Memory.TryRemove(url, out _); // transient network failure: next visual load can retry
        return image;
    }

    public static void Invalidate(string url)
    {
        Memory.TryRemove(url, out _);
        try
        {
            var file = CachePath(url);
            if (File.Exists(file)) File.Delete(file);
        }
        catch { /* a stale cache file is harmless */ }
    }

    private static async Task<BitmapSource?> LoadAsync(string url)
    {
        var path = CachePath(url);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            byte[]? bytes = null;
            if (File.Exists(path))
            {
                try { bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false); }
                catch { /* corrupt/unreadable cache: fetch the source again */ }
            }
            var fromDiskCache = bytes is not null;
            bytes ??= await Http.GetByteArrayAsync(url).ConfigureAwait(false);

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 192; // enough for 84px profile at high DPI; tiny decode versus originals
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (!fromDiskCache)
            {
                // Persist a tiny decoded thumbnail, not the original full-resolution photo.
                var encoder = new JpegBitmapEncoder { QualityLevel = 78 };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var thumbnail = new MemoryStream();
                encoder.Save(thumbnail);
                try { await File.WriteAllBytesAsync(path, thumbnail.ToArray()).ConfigureAwait(false); }
                catch { /* memory cache still works */ }
            }
            return bitmap;
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* retry on next bind */ }
            return null;
        }
    }

    private static string CachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheDirectory, hash + ".img");
    }
}
