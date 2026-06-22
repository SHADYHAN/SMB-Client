import AppKit

final class RynatTableRowView: NSTableRowView {
    override var interiorBackgroundStyle: NSView.BackgroundStyle {
        .normal
    }

    override func drawSelection(in dirtyRect: NSRect) {
        guard selectionHighlightStyle != .none else {
            return
        }

        let fillColor = isEmphasized ? RynatUI.selectionFillEmphasized : RynatUI.selectionFill
        fillColor.setFill()
        NSBezierPath(roundedRect: bounds.insetBy(dx: 6, dy: 2), xRadius: 8, yRadius: 8).fill()
    }
}

final class RynatSidebarRowView: NSTableRowView {
    override var interiorBackgroundStyle: NSView.BackgroundStyle {
        .normal
    }

    override func drawSelection(in dirtyRect: NSRect) {
        guard selectionHighlightStyle != .none else {
            return
        }

        let fillColor = isEmphasized ? RynatUI.selectionFillEmphasized : RynatUI.selectionFill
        fillColor.setFill()
        NSBezierPath(roundedRect: bounds.insetBy(dx: 8, dy: 3), xRadius: 7, yRadius: 7).fill()
    }
}
