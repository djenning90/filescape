using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileScape.Core.Audio;
using FileScape.Core.Layout;
using FileScape.Core.Model;
using FileScape.Core.Places;
using FileScape.Core.Planets;
using FileScape.Core.Units;
using Xunit;

namespace FileScape.Core.Tests;

/// <summary>
/// A behaviour baseline for refactors that must not change results (2026-11-14, the documentation pass): it writes the
/// exact outputs of every layout and composer, the walkers' trajectories and a hash of rendered audio for a fixed set of
/// trees to a text file, so a run before and a run after a change can be compared with <c>diff</c>. Opt-in: it runs only
/// when <c>FILESCAPE_BASELINE_OUT</c> names the output file. Floats are written in round-trip form, so any change to an
/// operation order that alters a result shows up.
/// </summary>
public class BehaviourBaselineTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        MaxDepth = 64,
    };

    /// <summary>Trees that exercise the layouts: wide, deep, mixed content, a repository, links and unlisted folders.</summary>
    private static IEnumerable<(string Name, FsTree Tree)> Trees()
    {
        yield return ("wide-40", Spec.WithDirs("wide", 40).Build());
        yield return ("deep-9", Spec.Chain("deep", 9).Build());
        var mixed = new Spec("mixed").File("a.png", 2_000).File("b.jpg", 30_000).File("c.md", 900).File("d.mp3", 4_000_000).File("e.zip", 70_000_000).File("f.cs", 3_000);
        for (var i = 0; i < 12; i++) mixed = mixed.Dir(Spec.WithFiles($"sub-{i:D2}", i * 3));
        mixed = mixed.Dir(new Spec("unlisted") { State = DirScanState.Unlisted }).Dir(new Spec("link") { IsLink = true });
        yield return ("mixed", mixed.Build());
        var repo = new Spec("repo").Dir(new Spec(".git")).Dir(new Spec("src").File("Program.cs")).Dir(new Spec("tests")).Dir(new Spec("docs")).File("README.md").File("repo.sln").File("repo.csproj");
        yield return ("repo", repo.Build());
        var many = new Spec("many");
        for (var i = 0; i < 300; i++) many = many.File($"item-{i:D3}.{(i % 5 == 0 ? "png" : i % 5 == 1 ? "txt" : i % 5 == 2 ? "wav" : i % 5 == 3 ? "zip" : "csv")}", 100 + i * 977);
        for (var i = 0; i < 30; i++) many = many.Dir(Spec.WithDirs($"d{i:D2}", i % 4));
        yield return ("many", many.Build());
    }

    [Fact]
    public void Write_the_baseline_when_asked()
    {
        var path = Environment.GetEnvironmentVariable("FILESCAPE_BASELINE_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var sb = new StringBuilder();
        void Dump(string label, Func<object?> make)
        {
            string text;
            try { text = JsonSerializer.Serialize(make(), Json); }
            catch (Exception e) { text = "ERROR " + e.GetType().Name + ": " + e.Message; }
            sb.Append("== ").Append(label).Append('\n').Append(text).Append('\n');
        }

        foreach (var (name, tree) in Trees())
        {
            Dump($"{name} universe", () => UniverseLayout.Compute(tree));
            Dump($"{name} universe pinned", () => UniverseLayout.Compute(tree, pinned: new HashSet<int>(tree.Nodes.Where(n => n.IsDirectory).Select(n => n.Id).Take(3))));
            Dump($"{name} tree layout", () => TreeLayout.Compute(tree, new LayoutParams()));
            Dump($"{name} world layout", () => WorldLayout.Compute(tree));
            Dump($"{name} planet", () => PlanetComposer.Compose(tree, 0));
            Dump($"{name} place", () => PlaceComposer.Compose(tree, 0));
            Dump($"{name} horizon", () => Horizon.Compose(tree, PlaceComposer.Compose(tree, 0), PlaceParams.Default));
            Dump($"{name} character", () => tree.Nodes.Where(n => n.IsDirectory).Select(n => WorldCharacter.Of(tree, n.Id, 0)).ToList());
            Dump($"{name} bench", () => ProjectBench.KeyLocations(tree, 0));

            // The sphere walker: a run with a held jump and a twirl, then a turn, from the arrival.
            Dump($"{name} sphere walk", () =>
            {
                var c = PlanetComposer.Compose(tree, 0);
                var w = new SphereWalker();
                w.Teleport(c.Surface, c.Arrival.Up, c.Arrival.Facing);
                var frame = Durations.FromSeconds(1 / 60.0);
                var trace = new List<float>();
                for (var i = 0; i < 240; i++)
                {
                    var wish = i < 160 ? w.Facing : Vector3.Normalize(Vector3.Cross(w.Up, w.Facing));
                    w.Update(new SphereWalkInput(wish, i > 60 && i < 120, i == 20 || i == 32, i >= 20 && i < 50), frame);
                    if (i % 10 == 0) trace.AddRange([w.Feet.X, w.Feet.Y, w.Feet.Z, w.Height.Value]);
                }

                return trace;
            });

            // The flat walker on the place's terrain: forward, a jump, a turn.
            Dump($"{name} place walk", () =>
            {
                var place = PlaceComposer.Compose(tree, 0);
                var w = new Walker();
                w.TeleportToEye(place.Terrain, place.Arrival.Eye, place.Arrival.Yaw, place.Arrival.LookPitch);
                var frame = Durations.FromSeconds(1 / 60.0);
                var trace = new List<float>();
                for (var i = 0; i < 240; i++)
                {
                    w.Update(place.Terrain, new WalkInput(1, i > 120 ? 0.5f : 0, i > 150 ? 0.6f : 0, i > 60 && i < 100, i == 30), frame);
                    if (i % 10 == 0) trace.AddRange([w.Feet.X, w.Feet.Y, w.Feet.Z]);
                }

                return trace;
            });
        }

        // The music: one hash per identity, with cues, so any change to the audio maths shows.
        foreach (var c in new[] { new WorldCharacter(WorldKind.Hub, 0, 0, -1), new WorldCharacter(WorldKind.WindowsRoot, 0, 0, -1), new WorldCharacter(WorldKind.WslRoot, 0, 0, -1), new WorldCharacter(WorldKind.Repository, 2, 0, 5), new WorldCharacter(WorldKind.Folder, 3, 2, -1), new WorldCharacter(WorldKind.Folder, 7, 4, 5) })
        {
            var synth = new SkySynth(seed: 7, theme: PlanetTheme.Library, character: c);
            synth.SetWorld(PlanetTheme.Library, c, 1);
            var buf = new float[4410 * 2];
            using var sha = SHA256.Create();
            for (var block = 0; block < 60; block++)
            {
                if (block == 5) synth.Jump();
                if (block == 12) synth.WindUp(TimeSpan.FromSeconds(0.4));
                if (block == 17) synth.Launch();
                if (block == 30) { synth.Land(6); synth.Arrive(); }
                if (block == 40) synth.Select();
                synth.Render(buf);
                var bytes = new byte[buf.Length * 4];
                Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            sha.TransformFinalBlock([], 0, 0);
            sb.Append("== audio ").Append(c.Kind).Append(' ').Append(c.Band).Append('\n').Append(Convert.ToHexString(sha.Hash!)).Append('\n');
        }

        File.WriteAllText(path, sb.ToString());
    }
}
