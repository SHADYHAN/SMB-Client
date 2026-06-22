use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::link::QuickLinkTarget;

pub const DEFAULT_PREVIEW_MAX_EDGE_PX: u32 = 512;
pub const MIN_PREVIEW_MAX_EDGE_PX: u32 = 64;
pub const MAX_PREVIEW_MAX_EDGE_PX: u32 = 2048;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum PreviewContentType {
    #[serde(rename = "image")]
    Image,
    #[serde(rename = "video")]
    Video,
    #[serde(rename = "pdf")]
    Pdf,
    #[serde(rename = "unsupported")]
    Unsupported,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum PreviewKind {
    #[serde(rename = "image_thumbnail")]
    ImageThumbnail,
    #[serde(rename = "video_poster")]
    VideoPoster,
    #[serde(rename = "video_stream")]
    VideoStream,
    #[serde(rename = "pdf")]
    Pdf,
    #[serde(rename = "unsupported")]
    Unsupported,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PreviewRequest {
    pub target: QuickLinkTarget,
    pub max_edge_px: u32,
}

impl PreviewRequest {
    pub fn new(target: QuickLinkTarget, max_edge_px: u32) -> Self {
        Self {
            target,
            max_edge_px: max_edge_px.clamp(MIN_PREVIEW_MAX_EDGE_PX, MAX_PREVIEW_MAX_EDGE_PX),
        }
    }

    pub fn thumbnail(target: QuickLinkTarget) -> Self {
        Self::new(target, DEFAULT_PREVIEW_MAX_EDGE_PX)
    }

    pub fn plan(&self) -> PreviewPlan {
        PreviewPlan::from_request(self)
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PreviewAsset {
    pub kind: PreviewKind,
    pub url: String,
    pub cache_key: String,
    pub width: Option<u32>,
    pub height: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PreviewPlan {
    pub target: QuickLinkTarget,
    pub content_type: PreviewContentType,
    pub cache_key: String,
    pub max_edge_px: u32,
    pub thumbnail: Option<PreviewAsset>,
    pub playback: Option<PreviewAsset>,
}

impl PreviewPlan {
    pub fn from_request(request: &PreviewRequest) -> Self {
        let content_type = infer_preview_content_type(&request.target);
        let cache_key = preview_cache_key(&request.target, request.max_edge_px);
        let thumbnail_url = format!("rynat-preview://thumbnail/{cache_key}");
        let playback_url = format!("rynat-preview://play/{cache_key}");

        let thumbnail = match content_type {
            PreviewContentType::Image => Some(PreviewAsset {
                kind: PreviewKind::ImageThumbnail,
                url: thumbnail_url,
                cache_key: cache_key.clone(),
                width: None,
                height: None,
            }),
            PreviewContentType::Video => Some(PreviewAsset {
                kind: PreviewKind::VideoPoster,
                url: thumbnail_url,
                cache_key: cache_key.clone(),
                width: None,
                height: None,
            }),
            PreviewContentType::Pdf => Some(PreviewAsset {
                kind: PreviewKind::Pdf,
                url: thumbnail_url,
                cache_key: cache_key.clone(),
                width: None,
                height: None,
            }),
            PreviewContentType::Unsupported => None,
        };

        let playback = match content_type {
            PreviewContentType::Video => Some(PreviewAsset {
                kind: PreviewKind::VideoStream,
                url: playback_url,
                cache_key: cache_key.clone(),
                width: None,
                height: None,
            }),
            _ => None,
        };

        Self {
            target: request.target.clone(),
            content_type,
            cache_key,
            max_edge_px: request.max_edge_px,
            thumbnail,
            playback,
        }
    }

    pub fn is_supported(&self) -> bool {
        self.content_type != PreviewContentType::Unsupported
    }
}

pub fn infer_preview_content_type(target: &QuickLinkTarget) -> PreviewContentType {
    if target.kind == crate::link::LinkKind::Directory {
        return PreviewContentType::Unsupported;
    }

    match file_extension(&target.path).as_deref() {
        Some(ext) if is_image_extension(ext) => PreviewContentType::Image,
        Some(ext) if is_video_extension(ext) => PreviewContentType::Video,
        Some("pdf") => PreviewContentType::Pdf,
        _ => PreviewContentType::Unsupported,
    }
}

pub fn preview_cache_key(target: &QuickLinkTarget, max_edge_px: u32) -> String {
    let canonical = format!(
        "preview-v1\0{}\0{}\0{}\0{:?}\0{}",
        target.server_host.trim().to_ascii_lowercase(),
        target.share.trim().to_ascii_lowercase(),
        target.path,
        target.kind,
        max_edge_px.clamp(MIN_PREVIEW_MAX_EDGE_PX, MAX_PREVIEW_MAX_EDGE_PX)
    );
    format!(
        "preview-v1-{}",
        Uuid::new_v5(&Uuid::NAMESPACE_URL, canonical.as_bytes())
    )
}

pub fn is_image_extension(ext: &str) -> bool {
    matches!(
        ext.to_ascii_lowercase().as_str(),
        "jpg" | "jpeg" | "png" | "gif" | "webp" | "bmp" | "tif" | "tiff" | "heic" | "heif" | "avif"
    )
}

pub fn is_video_extension(ext: &str) -> bool {
    matches!(
        ext.to_ascii_lowercase().as_str(),
        "mp4" | "mov" | "m4v" | "mkv" | "avi" | "webm" | "wmv" | "flv" | "ts" | "mts" | "m2ts"
    )
}

fn file_extension(path: &str) -> Option<String> {
    let file_name = path.rsplit('/').next()?;
    let (_, ext) = file_name.rsplit_once('.')?;
    if ext.is_empty() {
        None
    } else {
        Some(ext.to_ascii_lowercase())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::link::{LinkKind, QuickLinkTarget};

    #[test]
    fn plans_image_thumbnail_without_playback() {
        let target =
            QuickLinkTarget::new("nas", "Media", "/Photos/IMG_001.HEIC", None, LinkKind::File);
        let plan = PreviewRequest::thumbnail(target).plan();

        assert_eq!(plan.content_type, PreviewContentType::Image);
        assert!(plan.is_supported());
        assert_eq!(
            plan.thumbnail.as_ref().unwrap().kind,
            PreviewKind::ImageThumbnail
        );
        assert!(plan.playback.is_none());
    }

    #[test]
    fn plans_video_poster_and_playback() {
        let target = QuickLinkTarget::new("nas", "Media", "/Movies/demo.MP4", None, LinkKind::File);
        let plan = PreviewRequest::thumbnail(target).plan();

        assert_eq!(plan.content_type, PreviewContentType::Video);
        assert_eq!(
            plan.thumbnail.as_ref().unwrap().kind,
            PreviewKind::VideoPoster
        );
        assert_eq!(
            plan.playback.as_ref().unwrap().kind,
            PreviewKind::VideoStream
        );
        assert_eq!(
            plan.thumbnail.as_ref().unwrap().cache_key,
            plan.playback.as_ref().unwrap().cache_key
        );
    }

    #[test]
    fn directories_do_not_create_preview_assets() {
        let target = QuickLinkTarget::new("nas", "Media", "/Movies", None, LinkKind::Directory);
        let plan = PreviewRequest::thumbnail(target).plan();

        assert_eq!(plan.content_type, PreviewContentType::Unsupported);
        assert!(plan.thumbnail.is_none());
        assert!(plan.playback.is_none());
    }

    #[test]
    fn cache_key_is_stable_for_same_remote_asset() {
        let first = QuickLinkTarget::new(
            "NAS.local",
            "Media",
            "/Movies/demo.mp4",
            Some("demo.mp4".to_string()),
            LinkKind::File,
        );
        let second = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );

        assert_eq!(
            preview_cache_key(&first, DEFAULT_PREVIEW_MAX_EDGE_PX),
            preview_cache_key(&second, DEFAULT_PREVIEW_MAX_EDGE_PX)
        );
    }

    #[test]
    fn cache_key_normalizes_share_case() {
        let first = QuickLinkTarget::new(
            "nas.local",
            "Media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );
        let second = QuickLinkTarget::new(
            "nas.local",
            "media",
            "/Movies/demo.mp4",
            None,
            LinkKind::File,
        );

        assert_eq!(
            preview_cache_key(&first, DEFAULT_PREVIEW_MAX_EDGE_PX),
            preview_cache_key(&second, DEFAULT_PREVIEW_MAX_EDGE_PX)
        );
    }

    #[test]
    fn preview_size_is_clamped() {
        let target = QuickLinkTarget::new("nas", "Media", "/Photos/a.jpg", None, LinkKind::File);

        assert_eq!(
            PreviewRequest::new(target.clone(), 1).max_edge_px,
            MIN_PREVIEW_MAX_EDGE_PX
        );
        assert_eq!(
            PreviewRequest::new(target, 9_999).max_edge_px,
            MAX_PREVIEW_MAX_EDGE_PX
        );
    }
}
