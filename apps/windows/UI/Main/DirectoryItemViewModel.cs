using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Rynat.WindowsClient.UI.Main;

public sealed class DirectoryItemViewModel
{
    private static readonly SolidColorBrush DirectoryAccentBrush = new(ColorHelper.FromArgb(255, 36, 148, 105));
    private static readonly SolidColorBrush FileAccentBrush = new(ColorHelper.FromArgb(255, 71, 85, 105));
    private static readonly SolidColorBrush ImageAccentBrush = new(ColorHelper.FromArgb(255, 88, 70, 155));
    private static readonly SolidColorBrush VideoAccentBrush = new(ColorHelper.FromArgb(255, 216, 178, 20));
    private static readonly SolidColorBrush PdfAccentBrush = new(ColorHelper.FromArgb(255, 190, 78, 64));
    private static readonly SolidColorBrush OfficeAccentBrush = new(ColorHelper.FromArgb(255, 37, 99, 199));

    public DirectoryItemViewModel(
        string name,
        string displayPath,
        string remotePath,
        string shareName,
        bool isDirectory,
        ulong? sizeBytes,
        DateTimeOffset? modifiedAt
    )
    {
        Name = name;
        DisplayPath = displayPath;
        RemotePath = remotePath;
        ShareName = shareName;
        IsDirectory = isDirectory;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
    }

    public string Name { get; }

    public string DisplayPath { get; }

    public string RemotePath { get; }

    public string ShareName { get; }

    public bool IsDirectory { get; }

    public ulong? SizeBytes { get; }

    public DateTimeOffset? ModifiedAt { get; }

    public string IconGlyph
    {
        get
        {
            if (IsDirectory)
            {
                return "\uE8B7";
            }

            return FileKind switch
            {
                DirectoryItemFileKind.Image => "\uEB9F",
                DirectoryItemFileKind.Video => "\uE714",
                DirectoryItemFileKind.Pdf => "\uEA90",
                DirectoryItemFileKind.Spreadsheet => "\uE80A",
                DirectoryItemFileKind.Document => "\uE8A5",
                DirectoryItemFileKind.Presentation => "\uE8A5",
                DirectoryItemFileKind.Archive => "\uE8A5",
                DirectoryItemFileKind.Audio => "\uE8D6",
                _ => "\uE8A5",
            };
        }
    }

    public Brush AccentBrush
    {
        get
        {
            if (IsDirectory)
            {
                return DirectoryAccentBrush;
            }

            return FileKind switch
            {
                DirectoryItemFileKind.Image => ImageAccentBrush,
                DirectoryItemFileKind.Video => VideoAccentBrush,
                DirectoryItemFileKind.Pdf => PdfAccentBrush,
                DirectoryItemFileKind.Spreadsheet => OfficeAccentBrush,
                DirectoryItemFileKind.Document => OfficeAccentBrush,
                DirectoryItemFileKind.Presentation => OfficeAccentBrush,
                _ => FileAccentBrush,
            };
        }
    }

    public FontWeight NameFontWeight => IsDirectory
        ? new FontWeight { Weight = 600 }
        : new FontWeight { Weight = 400 };

    public Thickness RowPadding => new(6, 0, 6, 0);

    public string TypeLabel
    {
        get
        {
            if (IsDirectory)
            {
                return "文件夹";
            }

            return FileKind switch
            {
                DirectoryItemFileKind.Image => "图片",
                DirectoryItemFileKind.Video => "视频",
                DirectoryItemFileKind.Pdf => "PDF",
                DirectoryItemFileKind.Spreadsheet => "表格",
                DirectoryItemFileKind.Document => "文档",
                DirectoryItemFileKind.Presentation => "演示文稿",
                DirectoryItemFileKind.Archive => "压缩包",
                DirectoryItemFileKind.Audio => "音频",
                _ => "文件",
            };
        }
    }

    public string SizeLabel
    {
        get
        {
            if (IsDirectory)
            {
                return "--";
            }

            return SizeBytes is ulong size
                ? FormatBytes(size)
                : "未知";
        }
    }

    public string ModifiedLabel => ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "--";

    private DirectoryItemFileKind FileKind => KindForExtension(Path.GetExtension(Name));

    private static DirectoryItemFileKind KindForExtension(string? extension)
    {
        var normalized = extension?.TrimStart('.').ToLowerInvariant();
        return normalized switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "webp" or "heic" or "heif" or "avif" or "bmp" or "tif" or "tiff" => DirectoryItemFileKind.Image,
            "mp4" or "mov" or "m4v" or "mkv" or "avi" or "webm" or "wmv" or "flv" => DirectoryItemFileKind.Video,
            "pdf" => DirectoryItemFileKind.Pdf,
            "xls" or "xlsx" or "csv" or "tsv" => DirectoryItemFileKind.Spreadsheet,
            "doc" or "docx" or "txt" or "rtf" or "md" => DirectoryItemFileKind.Document,
            "ppt" or "pptx" or "key" => DirectoryItemFileKind.Presentation,
            "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "tgz" => DirectoryItemFileKind.Archive,
            "mp3" or "wav" or "aac" or "flac" or "m4a" or "ogg" => DirectoryItemFileKind.Audio,
            _ => DirectoryItemFileKind.File,
        };
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}

internal enum DirectoryItemFileKind
{
    File,
    Image,
    Video,
    Pdf,
    Spreadsheet,
    Document,
    Presentation,
    Archive,
    Audio,
}
