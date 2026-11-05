using System.Globalization;
using System.Numerics;
using FileScape.Core.Interaction;
using FileScape.Core.Places;
using FileScape.Core.Units;
using Xunit.Abstractions;

namespace FileScape.Core.Tests;

/// <summary>
/// Behaviour preservation for the units refactor (2026-11-05): numbers captured from the pre-refactor code (commit
/// 7079862) for a scripted walker run and for the camera math. The refactor changes types and names, not results.
/// Tolerances: 1e-4 m / 1e-3° on positions and angles after ~200 fixed sub-steps of float arithmetic.
/// </summary>
public class GoldenBehaviourTests(ITestOutputHelper output)
{
    internal static (Vector3 Feet, float Yaw, float LookPitch, int Jumps, int Landings, Vector3 Eye)[] ScriptedWalk()
    {
        var s = Spec.WithFiles("Golden", 40);
        for (var i = 0; i < 6; i++) s.Dir(new Spec($"dir-{i}").File("a.txt"));
        var tree = new Spec("parent").Dir(s).Build();
        var place = PlaceComposer.Compose(tree, tree.Root.Children[0]);
        var w = new Walker();
        w.TeleportToEye(place.Terrain, place.Arrival.Eye, place.Arrival.Yaw, place.Arrival.LookPitch);
        var marks = new List<(Vector3, float, float, int, int, Vector3)>();
        void Run(WalkInput input, int frames)
        {
            for (var i = 0; i < frames; i++) w.Update(place.Terrain, input, Durations.FromSeconds(1 / 60.0));
            marks.Add((w.Feet, w.Yaw.Value, w.LookPitch.Value, w.Jumps, w.Landings, w.Eye));
        }

        Run(new WalkInput(1, 0, 0, false, false), 90);          // down the terrace stairs
        Run(new WalkInput(1, 0, 0.6f, false, false), 40);       // walking curve
        w.Look(Degrees.Of(-12), Degrees.Of(-30));
        Run(new WalkInput(1, 0, 0, true, true), 1);             // running jump
        Run(new WalkInput(1, 0.5f, 0, true, false), 50);        // air steering and landing
        Run(new WalkInput(0, -1, 0, false, false), 45);         // strafe
        Run(WalkInput.None, 30);                                // stop
        return [.. marks];
    }

    [Fact]
    public void The_scripted_walk_matches_the_pre_refactor_numbers()
    {
        var marks = ScriptedWalk();
        foreach (var m in marks)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"({m.Feet.X:R}f, {m.Feet.Y:R}f, {m.Feet.Z:R}f, {m.Yaw:R}f, {m.LookPitch:R}f, {m.Jumps}, {m.Landings}, {m.Eye.Y:R}f),"));
        }

        (float X, float Y, float Z, float Yaw, float Pitch, int Jumps, int Landings, float EyeY)[] expected =
        [
            (-3.982211E-06f, 0f, 39.534042f, -0f, -6f, 0, 0, 1.75f),
            (0.92612076f, 0f, 36.978718f, -47.999973f, -6f, 0, 0, 1.75f),
            (0.98251027f, 0.08479167f, 36.925602f, -59.999973f, -36f, 1, 0, 1.8347917f),
            (6.244893f, 0f, 35.570705f, -59.999973f, -36f, 1, 1, 1.75f),
            (5.3725514f, 0f, 33.05193f, -59.999973f, -36f, 1, 1, 1.75f),
            (5.2318926f, 0f, 32.808266f, -59.999973f, -36f, 1, 1, 1.75f),
        ];
        marks.Length.Should().Be(expected.Length);
        for (var i = 0; i < marks.Length; i++)
        {
            var (feet, yaw, pitch, jumps, landings, eye) = marks[i];
            var e = expected[i];
            feet.X.Should().BeApproximately(e.X, 1e-4f, $"mark {i}");
            feet.Y.Should().BeApproximately(e.Y, 1e-4f, $"mark {i}");
            feet.Z.Should().BeApproximately(e.Z, 1e-4f, $"mark {i}");
            yaw.Should().BeApproximately(e.Yaw, 1e-3f, $"mark {i}");
            pitch.Should().BeApproximately(e.Pitch, 1e-3f, $"mark {i}");
            jumps.Should().Be(e.Jumps);
            landings.Should().Be(e.Landings);
            eye.Y.Should().BeApproximately(e.EyeY, 1e-4f, $"mark {i}");
        }
    }

    [Fact]
    public void Camera_math_matches_the_pre_refactor_numbers()
    {
        var f = CameraMath.Forward(Degrees.Of(30), Degrees.Of(-20));
        var fly = CameraMath.FlyDelta(new Vector3(0.5f, 0.2f, 1), Degrees.Of(30), Degrees.Of(-20), 7, TimeSpan.FromSeconds(0.016));
        var ground = CameraMath.GroundDelta(new Vector3(1, 0, 1), Degrees.Of(-45), 12, TimeSpan.FromSeconds(0.5));
        var orbit = CameraMath.OrbitPosition(new Vector3(3, 1, -2), Degrees.Of(210), Degrees.Of(-35), 25);
        var target = CameraMath.TargetFromPose(orbit, Degrees.Of(210), Degrees.Of(-35), 25);
        float[] values = [f.X, f.Y, f.Z, fly.X, fly.Y, fly.Z, ground.X, ground.Y, ground.Z, orbit.X, orbit.Y, orbit.Z, target.X, target.Y, target.Z,
            CameraMath.StepFlySpeed(10, 2.5f), CameraMath.InitialFlySpeed(37), CameraMath.MaxOrbitLookPitchAboveGround(3, 10, 0.45f).Value];
        output.WriteLine(string.Join(", ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture) + "f")));
        float[] expected = [-0.4698463f, -0.34202015f, -0.81379765f, -0.0038415946f, -0.014812119f, -0.11094973f, 6f, 0f, 0f, -7.2394037f, 15.339413f, -19.735157f, 3f, 1f, -2f, 17.46928f, 22.2f, 14.773584f];
        for (var i = 0; i < values.Length; i++) values[i].Should().BeApproximately(expected[i], 1e-5f * MathF.Max(1, MathF.Abs(expected[i])), $"value {i}");
    }
}
