namespace ZenithClipper.Editing;

/// <summary>Where a transformed clip lands. Shared by the renderer and the monitor overlay so they always agree.</summary>
public static class TransformGeometry
{
    /// <summary>The cropped clip rectangle to draw, rotated about its own centre by <paramref name="rotation"/> degrees (clockwise),
    /// plus the anchor point on screen. Units are those of <paramref name="width"/> and <paramref name="height"/>.</summary>
    public static (double Left, double Top, double W, double H, double AnchorX, double AnchorY) Place(double width, double height, double x, double y, double scaleX, double scaleY, double rotation, ClipTransform t)
    {
        double w = width * scaleX, h = height * scaleY;
        double ax = width / 2 + x * width, ay = height / 2 + y * height;
        double left = ax - t.AnchorX * w, top = ay - t.AnchorY * h;
        left += w * t.CropLeft; top += h * t.CropTop;
        w *= Math.Max(0, 1 - t.CropLeft - t.CropRight); h *= Math.Max(0, 1 - t.CropTop - t.CropBottom);
        if (rotation != 0)
        {
            double r = rotation * Math.PI / 180, s = Math.Sin(r), k = Math.Cos(r);
            double dx = left + w / 2 - ax, dy = top + h / 2 - ay;
            double cx = ax + dx * k - dy * s, cy = ay + dx * s + dy * k;
            left = cx - w / 2; top = cy - h / 2;
        }
        return (left, top, w, h, ax, ay);
    }
}
