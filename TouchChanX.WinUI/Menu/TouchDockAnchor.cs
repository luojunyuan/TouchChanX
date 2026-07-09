using System.Text.Json.Serialization;
using Windows.Foundation;

namespace TouchChanX.WinUI.Menu;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TouchDockAnchor.Left), "left")]
[JsonDerivedType(typeof(TouchDockAnchor.Top), "top")]
[JsonDerivedType(typeof(TouchDockAnchor.Right), "right")]
[JsonDerivedType(typeof(TouchDockAnchor.Bottom), "bottom")]
[JsonDerivedType(typeof(TouchDockAnchor.TopLeft), "topLeft")]
[JsonDerivedType(typeof(TouchDockAnchor.TopRight), "topRight")]
[JsonDerivedType(typeof(TouchDockAnchor.BottomLeft), "bottomLeft")]
[JsonDerivedType(typeof(TouchDockAnchor.BottomRight), "bottomRight")]
public abstract record TouchDockAnchor
{
    public record Left(double Scale) : TouchDockAnchor;
    public record Top(double Scale) : TouchDockAnchor;
    public record Right(double Scale) : TouchDockAnchor;
    public record Bottom(double Scale) : TouchDockAnchor;
    public record TopLeft : TouchDockAnchor;
    public record TopRight : TouchDockAnchor;
    public record BottomLeft : TouchDockAnchor;
    public record BottomRight : TouchDockAnchor;

    public static TouchDockAnchor Default { get; } = new Left(0.5);

    public static Point ToTouchPosition(TouchDockAnchor anchor, Size containerSize) =>
        ToTouchPosition(anchor, containerSize, new Size(Shared.TouchSize, Shared.TouchSize), Shared.TouchSpacing);

    public static Point ToTouchPosition(TouchDockAnchor anchor, Size containerSize, Size touchSize, int spacing)
    {
        var width = containerSize.Width;
        var height = containerSize.Height;
        var alignRight = Math.Max(spacing, width - touchSize.Width - spacing);
        var alignBottom = Math.Max(spacing, height - touchSize.Height - spacing);

        return anchor switch
        {
            TopLeft => new Point(spacing, spacing),
            TopRight => new Point(alignRight, spacing),
            BottomLeft => new Point(spacing, alignBottom),
            BottomRight => new Point(alignRight, alignBottom),
            Left x => new Point(spacing, ScaleY(x.Scale)),
            Top x => new Point(ScaleX(x.Scale), spacing),
            Right x => new Point(alignRight, ScaleY(x.Scale)),
            Bottom x => new Point(ScaleX(x.Scale), alignBottom),
            _ => ToTouchPosition(Default, containerSize, touchSize, spacing),
        };

        double ScaleX(double scale) =>
            Math.Clamp(
                Math.Clamp(scale, 0.0, 1.0) * width - touchSize.Width / 2.0 - spacing,
                spacing,
                alignRight);

        double ScaleY(double scale) =>
            Math.Clamp(
                Math.Clamp(scale, 0.0, 1.0) * height - touchSize.Height / 2.0 - spacing,
                spacing,
                alignBottom);
    }

    /// <summary>
    /// 计算 Touch 在边缘时的停靠位置和比例。如果不在有效边缘范围内，则返回 Default。
    /// </summary>
    public static TouchDockAnchor SnapFromRect(Size containerSize, Rect touchRect)
    {
        const double tolerance = 0.01d;
        double spacing = Shared.TouchSpacing;

        var isAtLeft = IsSnapped(touchRect.X, spacing);
        var isAtTop = IsSnapped(touchRect.Y, spacing);
        var isAtRight = IsSnapped(touchRect.X, containerSize.Width - spacing - touchRect.Width);
        var isAtBottom = IsSnapped(touchRect.Y, containerSize.Height - spacing - touchRect.Height);

        return (isAtLeft, isAtTop, isAtRight, isAtBottom) switch
        {
            (true, true, _, _) => new TopLeft(),
            (true, _, _, true) => new BottomLeft(),
            (_, true, true, _) => new TopRight(),
            (_, _, true, true) => new BottomRight(),

            (true, _, _, _) => new Left(GetVerticalScale()),
            (_, true, _, _) => new Top(GetHorizontalScale()),
            (_, _, true, _) => new Right(GetVerticalScale()),
            (_, _, _, true) => new Bottom(GetHorizontalScale()),

            _ => Default
        };

        double GetVerticalScale() => (touchRect.Y + spacing + touchRect.Height / 2.0) / containerSize.Height;
        double GetHorizontalScale() => (touchRect.X + spacing + touchRect.Width / 2.0) / containerSize.Width;
        bool IsSnapped(double v, double t) => Math.Abs(v - t) <= tolerance;
    }
}
