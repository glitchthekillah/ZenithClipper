namespace ZenithClipper.Editing;

public sealed record TransitionDef(string Id, string Name, string Category, bool Audio);

/// <summary>Premiere's video and audio transitions that the engine can render (Morph Cut is AI-based and left out).</summary>
public static class TransitionCatalog
{
    public static readonly TransitionDef[] All =
    [
        new("cross_dissolve", "Cross Dissolve", "Dissolve", false),
        new("additive_dissolve", "Additive Dissolve", "Dissolve", false),
        new("film_dissolve", "Film Dissolve", "Dissolve", false),
        new("dip_black", "Dip to Black", "Dissolve", false),
        new("dip_white", "Dip to White", "Dissolve", false),
        new("wipe_left", "Wipe (left to right)", "Wipe", false),
        new("wipe_right", "Wipe (right to left)", "Wipe", false),
        new("wipe_down", "Wipe (top to bottom)", "Wipe", false),
        new("wipe_up", "Wipe (bottom to top)", "Wipe", false),
        new("wipe_diagonal", "Diagonal Wipe", "Wipe", false),
        new("barn_doors", "Barn Doors", "Wipe", false),
        new("iris_round", "Iris Round", "Iris", false),
        new("iris_box", "Iris Box", "Iris", false),
        new("iris_diamond", "Iris Diamond", "Iris", false),
        new("clock_wipe", "Clock Wipe", "Wipe", false),
        new("radial_wipe", "Radial Wipe", "Wipe", false),
        new("venetian_blinds", "Venetian Blinds", "Wipe", false),
        new("checker_wipe", "Checker Wipe", "Wipe", false),
        new("push_left", "Push (to left)", "Slide", false),
        new("push_right", "Push (to right)", "Slide", false),
        new("push_up", "Push (up)", "Slide", false),
        new("push_down", "Push (down)", "Slide", false),
        new("slide_left", "Slide (from right)", "Slide", false),
        new("slide_right", "Slide (from left)", "Slide", false),
        new("slide_up", "Slide (from bottom)", "Slide", false),
        new("slide_down", "Slide (from top)", "Slide", false),
        new("split", "Split", "Slide", false),
        new("zoom_in", "Cross Zoom (in)", "Zoom", false),
        new("zoom_out", "Cross Zoom (out)", "Zoom", false),
        new("constant_power", "Constant Power", "Crossfade", true),
        new("constant_gain", "Constant Gain", "Crossfade", true),
        new("exponential_fade", "Exponential Fade", "Crossfade", true),
    ];
    public static TransitionDef? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
