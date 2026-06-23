using System.Linq;
using Rynat.Client;
using Rynat.WindowsClient.AppServices.Preview;

namespace Rynat.WindowsClient.PlatformIntegration.Preview;

public sealed class WindowsPreviewSurface : IWindowsPreviewSurface
{
    public PreviewPaneState BuildPlaceholder(string title, string description)
    {
        return new PreviewPaneState(
            title,
            description,
            string.Empty
        );
    }

    public PreviewPaneState BuildFromPlan(PreviewPlan? plan, string remotePath)
    {
        if (plan is null)
        {
            return new PreviewPaneState(
                TargetName(remotePath, null),
                remotePath,
                string.Empty
            );
        }

        var targetName = TargetName(remotePath, plan.Target.Name);
        var contentType = DescribeContentType(plan.ContentType);
        var thumbnail = DescribeAsset(plan.Thumbnail);
        var playback = DescribeAsset(plan.Playback);

        return new PreviewPaneState(
            targetName,
            BuildMetaText(contentType, thumbnail, playback),
            string.Empty,
            DisplayState: PreviewDisplayState.Ready,
            IconGlyph: PreviewIconGlyph(plan.ContentType),
            IconBrushKey: PreviewIconBrushKey(plan.ContentType)
        );
    }

    private static string TargetName(string remotePath, string? planName)
    {
        if (!string.IsNullOrWhiteSpace(planName))
        {
            return planName;
        }

        return remotePath.Split('/').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? remotePath;
    }

    private static string DescribeContentType(string contentType)
    {
        return contentType switch
        {
            "image" => "图片",
            "video" => "视频",
            "pdf" => "PDF",
            "unsupported" => "暂不支持",
            _ => contentType
        };
    }

    private static string DescribeAsset(PreviewAsset? asset)
    {
        if (asset is null)
        {
            return "无";
        }

        var kind = asset.Kind switch
        {
            "image_thumbnail" => "缩略图",
            "video_poster" => "封面",
            "video_stream" => "可播放",
            "pdf" => "预览",
            "unsupported" => "不支持",
            _ => asset.Kind
        };

        if (asset.Width is uint width && asset.Height is uint height)
        {
            return $"{kind}（{width}x{height}）";
        }

        return kind;
    }

    private static string BuildMetaText(string contentType, string thumbnail, string playback)
    {
        if (playback != "无")
        {
            return $"{contentType} · {thumbnail} · {playback}";
        }

        return $"{contentType} · {thumbnail}";
    }

    private static string PreviewIconGlyph(string contentType)
    {
        return contentType switch
        {
            "image" => "\uEB9F",
            "video" => "\uE714",
            "pdf" => "\uEA90",
            _ => "\uE8A5"
        };
    }

    private static string PreviewIconBrushKey(string contentType)
    {
        return contentType switch
        {
            "image" or "video" or "pdf" => "RynatAccentBrush",
            _ => "RynatMutedBrush"
        };
    }
}
