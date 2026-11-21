using System.Numerics;
using FileScape.Core.Model;
using FileScape.Core.Places;

namespace FileScape.Core.Planets;

/// <summary>How a world in the sky relates to the world you stand on (AD-85): what the telescope says about it.</summary>
public enum SurveyRelation
{
    /// <summary>The world this one is inside (its parent folder), hanging behind you.</summary>
    Parent,

    /// <summary>A folder inside this one, hanging in the sky over its launcher.</summary>
    Child,

    /// <summary>A folder inside this one drawn as a small moon in the ring (beyond the hung worlds' budget).</summary>
    Moon,

    /// <summary>A link from this folder to another place.</summary>
    Link,
}

/// <summary>
/// A world the telescope could frame: its folder, name, relation, where it is drawn (the current planet's frame, metres —
/// its centre is the origin), its drawn radius (metres) and what is known of it.
/// </summary>
/// <param name="Folders">Folders directly inside it, as far as it is listed.</param>
/// <param name="Files">Files directly inside it, as far as it is listed.</param>
/// <param name="Visited">You have been there this session.</param>
/// <param name="Listed">Its contents have been listed (else the counts are unknown).</param>
/// <param name="Followable">For a link: it can be followed.</param>
public sealed record SurveyCandidate(int NodeId, string Name, SurveyRelation Relation, Vector3 Position, float Radius,
    int Folders = 0, int Files = 0, bool Visited = false, bool Listed = true, bool Followable = true)
{
    /// <summary>A candidate from the tree's node <paramref name="id"/>: its counts, whether it is listed, a link's followability.</summary>
    public static SurveyCandidate From(FsTree tree, int id, SurveyRelation relation, Vector3 position, float radius, bool visited)
    {
        var n = tree[id];
        int folders = 0, files = 0;
        foreach (var c in n.Children)
        {
            if (tree[c].IsDirectory) folders++;
            else files++;
        }

        var state = Listing.Of(n);
        var listed = state == ListingState.Link || !Listing.IsUnknown(state) || n.Children.Count > 0;
        var followable = n.Kind != NodeKind.Link || n.Link is { CanFollow: true };
        return new SurveyCandidate(id, tree.DisplayName(id), relation, position, radius, folders, files, visited, listed, followable);
    }
}

/// <summary>What a click at the eyepiece does (<see cref="TelescopeSurvey.Click"/>).</summary>
public enum TelescopeClick
{
    /// <summary>Nothing: no world there, or a single click on the world already framed.</summary>
    None,

    /// <summary>The view swings to the clicked world (it is framed now).</summary>
    Aim,

    /// <summary>A deliberate double-click on the framed world: fly there (the same departure as Enter).</summary>
    Travel,
}

/// <summary>How the telescope's view closed, for <see cref="TelescopeSurvey.SelectionAfter"/>.</summary>
public enum TelescopeExit
{
    /// <summary>Esc, E, J or a command that steps back first: you are back where you stood.</summary>
    StepBack,

    /// <summary>Enter or a double-click: the departure for the framed world.</summary>
    Travel,

    /// <summary>T: the overview, opened with the framed world chosen.</summary>
    Overview,

    /// <summary>Closed from outside (a navigation made elsewhere, the telescope gone): as a step back.</summary>
    Interrupted,
}

/// <summary>A framed world: the candidate and where it is seen from the eyepiece (bearing + right of where the telescope points, elevation above the horizon, degrees; distance, metres).</summary>
public sealed record SurveyTarget(SurveyCandidate World, float BearingDegrees, float ElevationDegrees, float DistanceMetres);

/// <summary>
/// The telescope's survey (AD-85, PLANET-125/126; DJ 2026-11-20: "a useful way to survey the surrounding worlds … frames
/// useful destinations and makes their relationship to the current world clearer"): the worlds in this sky the telescope
/// frames, in the order its keys step through them, where it starts, and what it says about each one.
/// <para>
/// It owns the choice and the words; the app owns the camera, the drawing and the keys. Targets are ordered left to right
/// round the sky as seen from the eyepiece (by bearing from where the telescope points), so ← and → sweep the sky the
/// way the view turns. It starts on the world already chosen (the shared selection) if it is in this sky, else the hung
/// world nearest where the telescope points, else the nearest moon, else the world above. A ring of many moons is cut to
/// <see cref="MaxRingMoons"/> (those nearest the telescope's view) and the rest are counted (the list, J, has them all).
/// </para>
/// <para>What a change must preserve: the order follows the view (a step never jumps the view back past a world it
/// skipped); every relation names the world you stand on, so the survey always says where things are from here.</para>
/// </summary>
public sealed class TelescopeSurvey
{
    /// <summary>At most this many moons of the ring are framed (beyond the hung worlds): stepping through dozens would bury the worlds that matter; the list has them all.</summary>
    public const int MaxRingMoons = 8;

    private readonly List<SurveyTarget> _targets;

    private TelescopeSurvey(List<SurveyTarget> targets, int index, int omittedMoons, int belowHorizon)
    {
        _targets = targets;
        Index = index;
        OmittedMoons = omittedMoons;
        BelowHorizon = belowHorizon;
    }

    /// <summary>Worlds left out because the planet stands between the eyepiece and them (they hang over the far side).</summary>
    public int BelowHorizon { get; }

    /// <summary>The framed worlds, left to right.</summary>
    public IReadOnlyList<SurveyTarget> Targets => _targets;

    /// <summary>The framed world's index (−1 when there is none).</summary>
    public int Index { get; private set; }

    /// <summary>The framed world, or null for an empty sky.</summary>
    public SurveyTarget? Current => Index >= 0 && Index < _targets.Count ? _targets[Index] : null;

    /// <summary>Nothing in this sky to frame.</summary>
    public bool IsEmpty => _targets.Count == 0;

    /// <summary>Moons of the ring left out of the survey (<see cref="MaxRingMoons"/>).</summary>
    public int OmittedMoons { get; }

    /// <summary>
    /// "2 of 5": where the framed world is in the sweep; with one world, that it is the only one (PLANET-189: "1 of 1"
    /// read as if ← → should do something); with none, nothing.
    /// </summary>
    public string Counter => IsEmpty ? "" : _targets.Count == 1 ? OnlyWorldText : $"{Index + 1} of {_targets.Count}";

    /// <summary>The counter when the sky holds one world to frame.</summary>
    public const string OnlyWorldText = "the only world in view";

    /// <summary>← → have somewhere to go: more than one world is framed (PLANET-189).</summary>
    public bool CanStep => _targets.Count > 1;

    /// <summary>
    /// Plans the survey from <paramref name="eye"/> (the eyepiece), the local <paramref name="up"/> and the direction the
    /// telescope <paramref name="pointing"/> (any vector; only its part along the ground counts). With
    /// <paramref name="planetRadius"/> (metres; the planet's centre is the frame's origin) a world whose line of sight
    /// passes through the planet is left out and counted in <see cref="BelowHorizon"/> (seen 2026-11-20: the view framed
    /// the Starter Sky's own ground haze where Music Box hung over the far side).
    /// </summary>
    public static TelescopeSurvey Plan(IEnumerable<SurveyCandidate> candidates, Vector3 eye, Vector3 up, Vector3 pointing, int preferredId = -1, float planetRadius = 0f)
    {
        var hidden = 0;
        up = Vector3.Normalize(up);
        var ahead = pointing - up * Vector3.Dot(pointing, up);
        ahead = ahead.LengthSquared() > 1e-10f ? Vector3.Normalize(ahead) : Perpendicular(up);
        var right = Vector3.Cross(ahead, up);
        var all = new List<SurveyTarget>();
        foreach (var c in candidates)
        {
            var to = c.Position - eye;
            var dist = to.Length();
            if (dist < 1e-4f) continue;
            if (planetRadius > 0 && Blocked(eye, c.Position, planetRadius))
            {
                hidden++;
                continue;
            }

            var dir = to / dist;
            var bearing = MathF.Atan2(Vector3.Dot(dir, right), Vector3.Dot(dir, ahead)) * 180 / MathF.PI;
            var elevation = MathF.Asin(Math.Clamp(Vector3.Dot(dir, up), -1, 1)) * 180 / MathF.PI;
            all.Add(new SurveyTarget(c, bearing, elevation, dist));
        }

        var moons = all.Where(t => t.World.Relation == SurveyRelation.Moon).OrderBy(t => MathF.Abs(t.BearingDegrees)).ToList();
        var kept = all.Where(t => t.World.Relation != SurveyRelation.Moon).Concat(moons.Take(MaxRingMoons))
            .OrderBy(t => t.BearingDegrees).ThenBy(t => t.World.NodeId).ToList();
        var index = kept.FindIndex(t => t.World.NodeId == preferredId);
        if (index < 0) index = Nearest(kept, SurveyRelation.Child, SurveyRelation.Link);
        if (index < 0) index = Nearest(kept, SurveyRelation.Moon);
        if (index < 0) index = Nearest(kept, SurveyRelation.Parent);
        return new TelescopeSurvey(kept, index, Math.Max(0, moons.Count - MaxRingMoons), hidden);
    }

    /// <summary>The segment from <paramref name="a"/> to <paramref name="b"/> passes within <paramref name="radius"/> of the origin.</summary>
    private static bool Blocked(Vector3 a, Vector3 b, float radius)
    {
        var d = b - a;
        var t = Math.Clamp(-Vector3.Dot(a, d) / MathF.Max(1e-9f, d.LengthSquared()), 0f, 1f);
        return (a + d * t).LengthSquared() < radius * radius;
    }

    /// <summary>The index of the target of one of <paramref name="kinds"/> nearest the telescope's view, or −1.</summary>
    private static int Nearest(List<SurveyTarget> targets, params SurveyRelation[] kinds)
    {
        var best = -1;
        for (var i = 0; i < targets.Count; i++)
        {
            if (!kinds.Contains(targets[i].World.Relation)) continue;
            if (best < 0 || MathF.Abs(targets[i].BearingDegrees) < MathF.Abs(targets[best].BearingDegrees)) best = i;
        }

        return best;
    }

    private static Vector3 Perpendicular(Vector3 v) => Vector3.Normalize(MathF.Abs(v.X) < 0.9f ? Vector3.Cross(v, Vector3.UnitX) : Vector3.Cross(v, Vector3.UnitY));

    /// <summary>
    /// Frames the next world to the right (+1) or left (−1), wrapping round the sweep. False, and no change, when there is
    /// nowhere to step (no world, or one: PLANET-189), so the app neither re-aims nor plays the step's sound.
    /// </summary>
    public bool Step(int direction)
    {
        if (!CanStep || direction == 0) return false;
        Index = ((Index + Math.Sign(direction)) % _targets.Count + _targets.Count) % _targets.Count;
        return true;
    }

    /// <summary>
    /// What a click on <paramref name="hitId"/> (−1: no world under the pointer) does at the eyepiece (PLANET-188, AD-98):
    /// a click on another world of the survey aims at it (and frames it now); a double-click on the world already framed
    /// travels there, as Enter does. A double-click whose first click aimed the view (<paramref name="firstClickAimed"/>)
    /// only aims: travel is for the world you have been looking at, never for one the view has not swung to yet.
    /// </summary>
    public TelescopeClick Click(int hitId, bool doubleClick, bool firstClickAimed)
    {
        if (hitId < 0 || Current is not { } framed) return TelescopeClick.None;
        if (framed.World.NodeId == hitId) return doubleClick && !firstClickAimed ? TelescopeClick.Travel : TelescopeClick.None;
        return Aim(hitId) ? TelescopeClick.Aim : TelescopeClick.None;
    }

    /// <summary>
    /// The shared selection once the view closes (PLANET-190, AD-98). Framing a world selects it, so the card names what
    /// the lens shows; but stepping back must not leave a far world selected for a later key to act on (2026-11-21: after
    /// the view closed, Enter opened the last framed folder with Windows). Going on to that world (Enter, a double-click)
    /// or to the overview (T, which opens with it chosen) keeps the framed world; every other way out (Esc, E, J, a
    /// command that steps back first, anything that closes the view from outside) gives back the selection from before.
    /// </summary>
    public static int SelectionAfter(TelescopeExit exit, int before, int framed) =>
        exit is TelescopeExit.Travel or TelescopeExit.Overview && framed >= 0 ? framed : before;

    /// <summary>
    /// The key line under the view for a sky of <paramref name="count"/> framed worlds (markup, <c>UiText</c>): ← → only
    /// when there is another world to swing to; zoom, look and travel only when there is a world to look at (PLANET-189).
    /// </summary>
    public static string KeysMarkup(int count) => count switch
    {
        0 => "{T} the whole overview   ·   {J} the list   ·   {Esc} step back",
        1 => "{W} {S} or wheel: zoom   ·   drag: look   ·   {Enter} or double-click: fly there   ·   {T} the whole overview   ·   {J} the list   ·   {Esc} step back",
        _ => "{←} {→} the next world   ·   {W} {S} or wheel: zoom   ·   drag: look   ·   {Enter} or double-click: fly there   ·   {T} the whole overview   ·   {J} the list   ·   {Esc} step back",
    };

    /// <summary>Frames the world <paramref name="nodeId"/> (a click on it); false, and no change, when it is not in the survey.</summary>
    public bool Aim(int nodeId)
    {
        var i = _targets.FindIndex(t => t.World.NodeId == nodeId);
        if (i < 0) return false;
        Index = i;
        return true;
    }

    /// <summary>How the world relates to <paramref name="here"/> (the world you stand on), in a few words.</summary>
    public static string RelationText(SurveyCandidate c, string here) => c.Relation switch
    {
        SurveyRelation.Parent => $"the world {here} is inside · U flies up to it",
        SurveyRelation.Moon => $"a moon in {here}'s ring: a small world inside it",
        SurveyRelation.Link => c.Followable ? $"a link from {here} to another place" : $"a link from {here} · it can't be followed",
        _ => $"a world inside {here}",
    };

    /// <inheritdoc cref="RelationText(SurveyCandidate, string)"/>
    public static string RelationText(SurveyTarget t, string here) => RelationText(t.World, here);

    /// <summary>What is known of it: "3 folders · 12 files", "empty", or that it is not listed yet; and whether you have been there.</summary>
    public static string DetailText(SurveyCandidate c)
    {
        var what = !c.Listed ? "not listed yet: going there lists it"
            : c.Folders == 0 && c.Files == 0 ? "empty"
            : $"{c.Folders} {(c.Folders == 1 ? "folder" : "folders")} · {c.Files} {(c.Files == 1 ? "file" : "files")}";
        return c.Visited ? what + " · you have been there" : what;
    }

    /// <summary>What the telescope says over an empty sky.</summary>
    public static string EmptyText(string here) => $"Nothing hangs in this sky: {here} has no folders inside it. J lists its files.";
}
