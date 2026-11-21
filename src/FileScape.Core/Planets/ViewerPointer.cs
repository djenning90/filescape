namespace FileScape.Core.Planets;

/// <summary>Where a pointer is over the large picture viewer, as its mouse rule tells the places apart (PLANET-162).</summary>
public enum ViewerZone
{
    /// <summary>On the picture (or its placeholder frame) away from the step edges: drag pans, double-click zooms.</summary>
    Picture,

    /// <summary>The left step edge (a ‹ shows on hover): a click is the previous picture.</summary>
    PreviousEdge,

    /// <summary>The right step edge (a › shows on hover): a click is the next picture.</summary>
    NextEdge,

    /// <summary>The dimmed backdrop outside the picture: a click goes back to the world.</summary>
    Backdrop,

    /// <summary>The rows of the caption bar above the stage and the key line below it: a press there does nothing (the
    /// bar's buttons take their own presses before the viewer sees them).</summary>
    Chrome,
}

/// <summary>A rectangle in viewport units (docs/units.md): left, top, width, height.</summary>
public readonly record struct ScreenRect(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// The large viewer's mouse rule (PLANET-162, AD-94): which part of the window a pointer is over, and so what a click
/// there does. A click on either side edge of the stage steps (as Photos' side arrows do), a click on the backdrop
/// outside the picture goes back to the world (the outside click DJ asked for, AD-83's rule applied to the viewer's own
/// backdrop), a click on the picture does nothing but begin a pan; the wheel zooms everywhere (unchanged, AD-78).
/// PictureViewer calls <see cref="Zone"/> for every press and hover and acts on the answer.
/// Preserve: the bars' buttons always win (<see cref="ViewerZone.Chrome"/>); the edges never cover more than a third
/// of the stage, so the middle of any picture stays a place to drag.
/// </summary>
public static class ViewerPointer
{
    /// <summary>Each step edge is this wide, viewport pixels: wide enough to hit without aiming (≈ a thumb's width on a
    /// 1600 px window), narrow enough that a fitted picture's middle stays draggable (rationale for the figure: by eye).</summary>
    public const float EdgeWidthPixels = 110f;

    /// <summary>On a narrow stage the edges shrink to this share of its width each, so together they never cover more than a third.</summary>
    public const float MaxEdgeShare = 1f / 6f;

    /// <summary>
    /// The zone at (<paramref name="x"/>, <paramref name="y"/>): <paramref name="stage"/> is the area between the bars
    /// where the picture is laid out, <paramref name="picture"/> where it is drawn now (zoomed and panned; it may reach
    /// past the stage), <paramref name="canStep"/> false when the folder has one picture (the edges are then picture or
    /// backdrop like anywhere else).
    /// </summary>
    public static ViewerZone Zone(float x, float y, ScreenRect stage, ScreenRect picture, bool canStep)
    {
        if (y < stage.Y || y >= stage.Y + stage.Height) return ViewerZone.Chrome;
        if (canStep)
        {
            // The window's side margins belong to the edges too: a click that misses the ‹ slightly still steps.
            var edge = MathF.Min(EdgeWidthPixels, stage.Width * MaxEdgeShare);
            if (x < stage.X + edge) return ViewerZone.PreviousEdge;
            if (x >= stage.X + stage.Width - edge) return ViewerZone.NextEdge;
        }

        if (!stage.Contains(x, y)) return ViewerZone.Backdrop; // the side margins beside the stage
        return picture.Contains(x, y) ? ViewerZone.Picture : ViewerZone.Backdrop;
    }
}
