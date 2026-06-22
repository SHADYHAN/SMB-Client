import AppKit
import AVFoundation
import Foundation
import QuickLookThumbnailing

enum MacPreviewError: Error, LocalizedError {
    case unsupported(String)
    case noImageGenerated

    var errorDescription: String? {
        switch self {
        case .unsupported(let kind):
            return "暂不支持生成 \(kind) 预览"
        case .noImageGenerated:
            return "系统没有返回缩略图"
        }
    }
}

struct RenderedPreview {
    let kind: String
    let sourceURL: URL
    let image: NSImage
    let width: Int
    let height: Int
}

final class MacPreviewService {
    func renderThumbnail(
        for fileURL: URL,
        plan: PreviewPlan,
        completion: @escaping (Result<RenderedPreview, Error>) -> Void
    ) {
        switch plan.contentType {
        case "image", "pdf":
            renderQuickLookThumbnail(for: fileURL, plan: plan, completion: completion)
        case "video":
            renderVideoPoster(for: fileURL, plan: plan, completion: completion)
        default:
            completion(.failure(MacPreviewError.unsupported(plan.contentType)))
        }
    }

    private func renderQuickLookThumbnail(
        for fileURL: URL,
        plan: PreviewPlan,
        completion: @escaping (Result<RenderedPreview, Error>) -> Void
    ) {
        let size = CGSize(width: 512, height: 512)
        let request = QLThumbnailGenerator.Request(
            fileAt: fileURL,
            size: size,
            scale: NSScreen.main?.backingScaleFactor ?? 2,
            representationTypes: .thumbnail
        )

        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { representation, error in
            if let error {
                completion(.failure(error))
                return
            }
            guard let image = representation?.nsImage else {
                completion(.failure(MacPreviewError.noImageGenerated))
                return
            }
            completion(.success(RenderedPreview(
                kind: plan.thumbnail?.kind ?? "thumbnail",
                sourceURL: fileURL,
                image: image,
                width: Int(image.size.width),
                height: Int(image.size.height)
            )))
        }
    }

    private func renderVideoPoster(
        for fileURL: URL,
        plan: PreviewPlan,
        completion: @escaping (Result<RenderedPreview, Error>) -> Void
    ) {
        let asset = AVURLAsset(url: fileURL)
        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: 512, height: 512)

        generator.generateCGImagesAsynchronously(forTimes: [NSValue(time: CMTime(seconds: 0.1, preferredTimescale: 600))]) { _, cgImage, _, _, error in
            if let error {
                completion(.failure(error))
                return
            }
            guard let cgImage else {
                completion(.failure(MacPreviewError.noImageGenerated))
                return
            }
            let image = NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
            completion(.success(RenderedPreview(
                kind: plan.thumbnail?.kind ?? "video_poster",
                sourceURL: fileURL,
                image: image,
                width: cgImage.width,
                height: cgImage.height
            )))
        }
    }
}
