using System.IO.Compression;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lychee.Platform;

namespace Lychee.Core;

/// <summary>
/// Menu bar / system tray icon built on Avalonia's cross-platform TrayIcon
/// (NSStatusItem on macOS, NotifyIcon equivalent on Windows).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon? _icon;
    private readonly MacStatusItem? _mac;
    private bool _disposed;

    public void ApplyStatusIcon() => _mac?.Apply();

    public event EventHandler? ShowHideRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                _mac = new MacStatusItem(LoadMenuIconPng());
                _mac.ShowHideRequested += (s, e) => ShowHideRequested?.Invoke(this, EventArgs.Empty);
                _mac.SettingsRequested += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
                _mac.ExitRequested += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.Error("tray", ex);
            }
            return;
        }

        WindowIcon? icon = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Lychee/Assets/icon.png"));
            icon = new WindowIcon(stream);
        }
        catch
        {
            // A missing icon must not prevent startup.
        }

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show / Hide");
        showItem.Click += (s, e) => ShowHideRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var quitItem = new NativeMenuItem("Quit Lychee");
        quitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(showItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _icon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Lychee",
            IsVisible = true,
            Menu = menu
        };
        _icon.Clicked += (s, e) => ShowHideRequested?.Invoke(this, EventArgs.Empty);

        var icons = new TrayIcons { _icon };
        TrayIcon.SetIcons(Application.Current!, icons);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mac?.Dispose();
        if (_icon == null) return;
        _icon.IsVisible = false;
        try
        {
            var icons = TrayIcon.GetIcons(Application.Current!);
            icons?.Remove(_icon);
        }
        catch { }
        _icon.Dispose();
    }

    /// <summary>
    /// Menu-bar glyph. 22px matches a square status item at 72 dpi, so
    /// NSImage does not need a separate point-size conversion. The flat
    /// off-white backdrop is removed so it is not a white tile.
    /// </summary>
    private static byte[] LoadMenuIconPng()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Lychee/Assets/icon.png"));
        using var scaled = Bitmap.DecodeToWidth(stream, 22, BitmapInterpolationMode.HighQuality);
        var w = scaled.PixelSize.Width;
        var h = scaled.PixelSize.Height;
        var stride = w * 4;
        var pixels = new byte[stride * h];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            scaled.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        if (scaled.Format == PixelFormat.Bgra8888)
        {
            for (var i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            int r = pixels[i], g = pixels[i + 1], b = pixels[i + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (min > 225 && max - min < 18)
                pixels[i + 3] = 0;
        }

        return EncodePng(pixels, w, h);
    }

    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        var raw = new byte[(width * 4 + 1) * height];
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
        {
            raw[y * (rowBytes + 1)] = 0;
            Buffer.BlockCopy(rgba, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(png, "IHDR"u8, ihdr);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBE(len, 0, data.Length);
        stream.Write(len);
        stream.Write(type);
        stream.Write(data);
        var crcBuf = new byte[type.Length + data.Length];
        type.CopyTo(crcBuf);
        data.CopyTo(crcBuf.AsSpan(type.Length));
        Span<byte> crc = stackalloc byte[4];
        WriteBE(crc, 0, (int)Crc32(crcBuf));
        stream.Write(crc);
    }

    private static void WriteBE(Span<byte> dest, int offset, int value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
