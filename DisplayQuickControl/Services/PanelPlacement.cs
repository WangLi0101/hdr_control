namespace DisplayQuickControl.Services;

// Coordinates remain in physical pixels throughout, including negative monitor origins.
internal static class PanelPlacement
{
    internal readonly record struct Box(int Left, int Top, int Right, int Bottom);
    internal static (int X, int Y) Calculate(Box monitor, Box work, Box anchor, int width, int height, int margin)
    {
        var insets = new[] { work.Left - monitor.Left, work.Top - monitor.Top, monitor.Right - work.Right, monitor.Bottom - work.Bottom };
        var edge = Array.IndexOf(insets, insets.Max());
        if (insets[edge] == 0)
        {
            // Auto-hidden taskbars: use the tray rectangle's nearest monitor edge.
            var distances = new[] { Math.Abs(anchor.Left - monitor.Left), Math.Abs(anchor.Top - monitor.Top), Math.Abs(monitor.Right - anchor.Right), Math.Abs(monitor.Bottom - anchor.Bottom) };
            edge = Array.IndexOf(distances, distances.Min());
        }
        var x = anchor.Right - width; var y = anchor.Top - height - margin;
        switch (edge)
        {
            case 0: x = work.Left + margin; y = anchor.Bottom - height; break;
            case 1: y = work.Top + margin; break;
            case 2: x = work.Right - width - margin; y = anchor.Bottom - height; break;
            case 3: y = work.Bottom - height - margin; break;
        }
        return (Math.Clamp(x, work.Left + margin, Math.Max(work.Left + margin, work.Right - width - margin)),
            Math.Clamp(y, work.Top + margin, Math.Max(work.Top + margin, work.Bottom - height - margin)));
    }
}
