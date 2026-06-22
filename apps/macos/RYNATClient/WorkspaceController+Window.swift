import AppKit
import Foundation

// MARK: - Window Delegate

extension WorkspaceController {
    func windowWillClose(_ notification: Notification) {
        if notification.object as? NSWindow === window {
            window?.orderOut(nil)
        }
    }

    func windowDidResize(_ notification: Notification) {
        applyWorkspaceSplitLayout()
        syncSidebarHeaderHeightToFileTable()
    }

    func windowWillStartLiveResize(_ notification: Notification) {
        isWindowLiveResizing = true
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        isWindowLiveResizing = false
        applyWorkspaceSplitLayout()
    }

    func windowWillUseStandardFrame(_ window: NSWindow, defaultFrame newFrame: NSRect) -> NSRect {
        window.screen?.visibleFrame ?? newFrame
    }

    func splitViewDidResizeSubviews(_ notification: Notification) {
        guard let splitView = notification.object as? NSSplitView, splitView === mainSplitView else {
            return
        }
        guard !isApplyingWorkspaceSplitLayout, !isWindowLiveResizing, hasAppliedWorkspaceSplitLayout else {
            return
        }
        captureWorkspaceSplitWidths()
    }

    func splitView(_ splitView: NSSplitView, constrainMinCoordinate proposedMinimumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat {
        guard splitView === mainSplitView else {
            return proposedMinimumPosition
        }
        if dividerIndex == 0 {
            return SidebarView.minimumWidth
        }
        let sidebarWidth = splitView.arrangedSubviews.first?.frame.width ?? sidebarPaneWidth
        return sidebarWidth + splitView.dividerThickness + fileWorkspaceMinimumWidth
    }

    func splitView(_ splitView: NSSplitView, constrainMaxCoordinate proposedMaximumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat {
        guard splitView === mainSplitView else {
            return proposedMaximumPosition
        }
        if dividerIndex == 0 {
            let dividerCount = CGFloat(max(0, splitView.arrangedSubviews.count - 1))
            let previewWidth = isPreviewVisible && splitView.arrangedSubviews.count >= 3 ? previewPaneMinimumWidth : 0
            let maxSidebarWidth = splitView.bounds.width - fileWorkspaceMinimumWidth - previewWidth - splitView.dividerThickness * dividerCount
            return min(SidebarView.maximumWidth, max(SidebarView.minimumWidth, maxSidebarWidth))
        }
        let maxPreviewPosition = splitView.bounds.width - previewPaneMinimumWidth - splitView.dividerThickness
        return min(proposedMaximumPosition, maxPreviewPosition)
    }
}
