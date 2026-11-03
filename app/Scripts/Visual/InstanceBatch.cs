using Godot;

namespace FileScape.App.Visual;

/// <summary>
/// Collects many box-like instances and builds them into chunked MultiMeshInstance3D nodes that share one material
/// (AD-06). Chunks follow insertion order. Callers add nodes in depth-first order, so a chunk covers a contiguous
/// subtree region and per-chunk frustum culling is effective. Each instance is 20 floats: a 3x4 transform, a
/// colour, and custom data.
/// </summary>
public sealed class InstanceBatch
{
    public const int ChunkSize = 4096;
    private const int Stride = 20;

    private float[] _data = new float[Stride * 256];

    public int Count { get; private set; }

    /// <summary>Adds an axis-aligned box. custom.xyz defaults to the scale (for edge/window shaders).</summary>
    public void AddBox(Vector3 center, Vector3 size, Color color, float style)
    {
        Add(new Vector3(size.X, 0, 0), new Vector3(0, size.Y, 0), new Vector3(0, 0, size.Z), center, color,
            new Color(size.X, size.Y, size.Z, style));
    }

    /// <summary>Adds an instance with an arbitrary basis, given as columns X, Y, Z.</summary>
    public void Add(Vector3 colX, Vector3 colY, Vector3 colZ, Vector3 origin, Color color, Color custom)
    {
        EnsureCapacity(Count + 1);
        var i = Count * Stride;
        // Godot's MultiMesh buffer stores the transform row-major: [Xx Yx Zx Ox, Xy Yy Zy Oy, Xz Yz Zz Oz].
        _data[i + 0] = colX.X; _data[i + 1] = colY.X; _data[i + 2] = colZ.X; _data[i + 3] = origin.X;
        _data[i + 4] = colX.Y; _data[i + 5] = colY.Y; _data[i + 6] = colZ.Y; _data[i + 7] = origin.Y;
        _data[i + 8] = colX.Z; _data[i + 9] = colY.Z; _data[i + 10] = colZ.Z; _data[i + 11] = origin.Z;
        _data[i + 12] = color.R; _data[i + 13] = color.G; _data[i + 14] = color.B; _data[i + 15] = color.A;
        _data[i + 16] = custom.R; _data[i + 17] = custom.G; _data[i + 18] = custom.B; _data[i + 19] = custom.A;
        Count++;
    }

    /// <summary>Adds a thin box running from a to b (a wire or street segment).</summary>
    public void AddSegment(Vector3 a, Vector3 b, float width, float height, Color color, float offsetAlong)
    {
        var d = b - a;
        var len = d.Length();
        if (len < 1e-4f)
        {
            return;
        }

        var dir = d / len;
        var side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() < 1e-6f)
        {
            side = Vector3.Right;
        }

        side = side.Normalized();
        var up = side.Cross(dir).Normalized();
        Add(dir * len, up * height, side * width, (a + b) / 2, color, new Color(len, offsetAlong, 0, 0));
    }

    private void EnsureCapacity(int instances)
    {
        if (instances * Stride > _data.Length)
        {
            Array.Resize(ref _data, Math.Max(instances * Stride, _data.Length * 2));
        }
    }

    /// <summary>
    /// Writes the batch into chunked MultiMeshInstance3D nodes under <paramref name="parent"/>. Nodes in
    /// <paramref name="pool"/> are reused, so re-layout during a scan replaces buffers instead of re-creating
    /// scene nodes. Surplus nodes are freed.
    /// </summary>
    public void BuildInto(List<MultiMeshInstance3D> pool, Node3D parent, Mesh mesh, Material material, bool castShadows = true)
    {
        var chunks = (Count + ChunkSize - 1) / ChunkSize;
        for (var i = 0; i < chunks; i++)
        {
            var start = i * ChunkSize;
            var n = Math.Min(ChunkSize, Count - start);
            if (i >= pool.Count)
            {
                var created = new MultiMeshInstance3D
                {
                    Multimesh = new MultiMesh
                    {
                        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                        UseColors = true,
                        UseCustomData = true,
                        Mesh = mesh,
                    },
                    MaterialOverride = material,
                    CastShadow = castShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                parent.AddChild(created);
                pool.Add(created);
            }

            var mm = pool[i].Multimesh;
            if (mm.InstanceCount != n)
            {
                mm.InstanceCount = n;
            }

            var slice = new float[n * Stride];
            Array.Copy(_data, start * Stride, slice, 0, slice.Length);
            mm.Buffer = slice;
        }

        for (var i = pool.Count - 1; i >= chunks; i--)
        {
            pool[i].QueueFree();
            pool.RemoveAt(i);
        }
    }
}
