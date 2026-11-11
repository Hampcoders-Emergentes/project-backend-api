namespace Hampcoders.Electrolink.API.StoreManagement.Domain.Model;

/// <summary>Footprint of an equipment on the kitchen floor plan, in layout units (top-left origin).</summary>
public sealed record EquipmentPlacement(double CoordinateX, double CoordinateY, double Width, double Height, int ZIndex)
{
    public static EquipmentPlacement Create(double x, double y, double width, double height, int zIndex)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentException("Layout coordinates must be finite numbers.");
        if (width <= 0 || height <= 0) throw new ArgumentException("width and height must be positive.");
        if (zIndex is < 0 or > 100) throw new ArgumentException("zIndex must be between 0 and 100.");
        return new(x, y, width, height, zIndex);
    }

    public double Right => CoordinateX + Width;
    public double Bottom => CoordinateY + Height;

    /// <summary>Strict overlap: rectangles that only share an edge do not overlap.</summary>
    public bool Overlaps(EquipmentPlacement other) =>
        CoordinateX < other.Right && other.CoordinateX < Right && CoordinateY < other.Bottom && other.CoordinateY < Bottom;
}

/// <summary>Domain service: validates a complete kitchen floor plan before it is saved.</summary>
public static class KitchenLayoutPolicy
{
    public const double MaxCanvasSize = 100_000;

    public static void ValidateCanvas(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || width > MaxCanvasSize || height > MaxCanvasSize)
            throw new ArgumentException($"The floor plan must be between 1 and {MaxCanvasSize:0} units wide and high.");
    }

    /// <summary>
    ///     Every placed equipment must lie inside the canvas, and equipment on the same layer (zIndex) must not overlap.
    ///     Different layers may overlap (e.g. a fryer under an extraction hood).
    /// </summary>
    public static void Validate(double canvasWidth, double canvasHeight, IReadOnlyList<(string EquipmentId, string Name, EquipmentPlacement Placement)> placed)
    {
        ValidateCanvas(canvasWidth, canvasHeight);
        var problems = new List<string>();
        foreach (var (id, name, p) in placed)
            if (p.CoordinateX < 0 || p.CoordinateY < 0 || p.Right > canvasWidth || p.Bottom > canvasHeight)
                problems.Add($"{name} ({id}) lies outside the {canvasWidth:0.##} x {canvasHeight:0.##} floor plan.");
        for (var i = 0; i < placed.Count; i++)
            for (var j = i + 1; j < placed.Count; j++)
                if (placed[i].Placement.ZIndex == placed[j].Placement.ZIndex && placed[i].Placement.Overlaps(placed[j].Placement))
                    problems.Add($"{placed[i].Name} ({placed[i].EquipmentId}) overlaps {placed[j].Name} ({placed[j].EquipmentId}) on layer {placed[i].Placement.ZIndex}.");
        if (problems.Count > 0) throw new ArgumentException("Invalid layout: " + string.Join(" ", problems.Take(10)));
    }
}
