using Godot;

namespace FileScape.App.Planets;

/// <summary>
/// A MultiMesh that draws only the instances alive this frame (PLANET-108, AD-81): the overview lays out thousands of
/// worlds but its level of detail draws a few hundred, so instead of re-sending every laid-out world each frame (most at
/// zero size), the caller writes the live ones one after another and only those are uploaded and drawn
/// (<see cref="MultiMesh.VisibleInstanceCount"/>). The instance buffer is Godot's flat layout: 12 floats of transform
/// (the 3×4 matrix row by row), then 4 of colour if used, then 4 of custom data if used.
/// <para>
/// Capacity only grows (doubling), so a frame that draws a few more worlds does not reallocate the GPU buffer each time.
/// Per frame: <see cref="Begin"/>, <see cref="Next"/> for each live instance (write at the returned offset into
/// <see cref="Buffer"/>), <see cref="End"/>. Main thread only.
/// </para>
/// </summary>
public sealed class LiveMultiMesh
{
    /// <summary>The smallest capacity allocated (instances): avoids reallocating for the first handful.</summary>
    private const int MinCapacity = 64;

    public LiveMultiMesh(MultiMesh mesh)
    {
        Mesh = mesh;
        Stride = 12 + (mesh.UseColors ? 4 : 0) + (mesh.UseCustomData ? 4 : 0);
        Mesh.InstanceCount = 0;
    }

    public MultiMesh Mesh { get; }

    /// <summary>Floats per instance in <see cref="Buffer"/>.</summary>
    public int Stride { get; }

    /// <summary>The instance buffer this frame (capacity × <see cref="Stride"/> floats; only the first Live are drawn).</summary>
    public float[] Buffer { get; private set; } = [];

    /// <summary>Instances written this frame.</summary>
    public int Live { get; private set; }

    public void Begin() => Live = 0;

    /// <summary>Reserves the next live instance and returns its offset in <see cref="Buffer"/>.</summary>
    public int Next()
    {
        if ((Live + 1) * Stride > Buffer.Length)
        {
            var capacity = Math.Max(MinCapacity, Buffer.Length / Stride * 2);
            var grown = new float[capacity * Stride];
            Array.Copy(Buffer, grown, Buffer.Length);
            Buffer = grown;
            Mesh.InstanceCount = capacity;
        }

        return Live++ * Stride;
    }

    /// <summary>Uploads the frame's instances and draws only them.</summary>
    public void End()
    {
        if (Buffer.Length > 0) Mesh.Buffer = Buffer;
        Mesh.VisibleInstanceCount = Live;
    }

    /// <summary>
    /// Writes a transform at <paramref name="o"/>: Godot's layout is the 3×4 matrix row by row, so each row holds the basis
    /// columns' components then the origin's.
    /// </summary>
    public static void WriteTransform(float[] buf, int o, Vector3 x, Vector3 y, Vector3 z, Vector3 origin)
    {
        buf[o] = x.X; buf[o + 1] = y.X; buf[o + 2] = z.X; buf[o + 3] = origin.X;
        buf[o + 4] = x.Y; buf[o + 5] = y.Y; buf[o + 6] = z.Y; buf[o + 7] = origin.Y;
        buf[o + 8] = x.Z; buf[o + 9] = y.Z; buf[o + 10] = z.Z; buf[o + 11] = origin.Z;
    }

    /// <summary>Writes a colour (RGBA floats) at <paramref name="o"/>.</summary>
    public static void WriteColor(float[] buf, int o, Color c)
    {
        buf[o] = c.R; buf[o + 1] = c.G; buf[o + 2] = c.B; buf[o + 3] = c.A;
    }
}
