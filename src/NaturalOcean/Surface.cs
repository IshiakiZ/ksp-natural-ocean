using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NaturalOcean
{
    /// <summary>
    /// The mesh the sea is drawn on: square rings round the point of the sea under the camera, each ring's cells twice as
    /// large as those of the ring inside it, the innermost a full square (nested grids, as Crest and others lay a sea out).
    /// Each ring is slid along with the camera in steps of two of its own cells, so that a point of the mesh stays at the same
    /// place in the waves and the next ring's points lie on every other one of this ring's. The shader moves the points with
    /// the waves, merges each ring's rim into the next ring, pushes the outermost rim to the horizon and bends the lot over
    /// the planet's curve (tools/shaderpack/ocean.glsl).
    /// </summary>
    sealed class Surface : IDisposable
    {
        // (192 cells across a ring since 2026-10-11: at 128 a breaking crest's lip, a metre or two of water, had two or three
        // of the mesh's points to bend over at sixty metres. More than 255 would need wider numbers for the mesh's corners.)
        public const int Cells = 192, Rings = 12;
        readonly GameObject root;
        readonly Transform[] ring = new Transform[Rings];
        readonly MeshRenderer[] drawn = new MeshRenderer[Rings];
        readonly MeshFilter[] shape = new MeshFilter[Rings];
        readonly Mesh whole, hollow, outermost;
        public readonly Material Material;
        int outer = -1;

        public Surface(Shader shader)
        {
            Material = new Material(shader) { name = "Natural Ocean: the sea", renderQueue = 2501 };
            // (the seaweed reads these from here each frame, the first time before the mirror has set them: Unity writes an
            // error to the log for each one a material was never given)
            Material.SetVector("_OceanMirrorX", Vector4.zero); Material.SetVector("_OceanMirrorY", Vector4.zero); Material.SetVector("_OceanMirrorW", Vector4.zero);
            whole = Make(false, false); hollow = Make(true, false); outermost = Make(true, true);
            root = new GameObject("Natural Ocean: the sea");
            for (int n = 0; n < Rings; n++)
            {
                var g = new GameObject("ring " + n) { layer = 15 };
                g.transform.SetParent(root.transform, false);
                shape[n] = g.AddComponent<MeshFilter>();
                shape[n].sharedMesh = n == 0 ? whole : hollow;
                drawn[n] = g.AddComponent<MeshRenderer>();
                drawn[n].sharedMaterial = Material;
                drawn[n].shadowCastingMode = ShadowCastingMode.Off;
                drawn[n].receiveShadows = false;
                drawn[n].lightProbeUsage = LightProbeUsage.Off;
                drawn[n].reflectionProbeUsage = ReflectionProbeUsage.Off;
                drawn[n].enabled = false;
                ring[n] = g.transform;
            }
        }

        /// <summary>A square of Cells by Cells cells with its middle at nought; hollow: without the middle that the ring inside covers (less one cell all round, so that the two always overlap); rim: its outermost points marked, to be pushed out to the horizon.</summary>
        static Mesh Make(bool hollow, bool rim)
        {
            int side = Cells + 1, half = Cells / 2, inner = Cells / 4 - 1;
            var points = new Vector3[side * side];
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                    points[z * side + x] = new Vector3(x - half, rim && (x == 0 || z == 0 || x == Cells || z == Cells) ? 1f : 0f, z - half);
            var faces = new System.Collections.Generic.List<int>(Cells * Cells * 6);
            for (int z = 0; z < Cells; z++)
                for (int x = 0; x < Cells; x++)
                {
                    if (hollow && x - half >= -inner && x - half < inner && z - half >= -inner && z - half < inner) continue;
                    int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                    // (the diagonal turned in a chequer, so that no direction of wave is favoured)
                    if (((x + z) & 1) == 0) { faces.Add(a); faces.Add(c); faces.Add(b); faces.Add(b); faces.Add(c); faces.Add(d); }
                    else { faces.Add(a); faces.Add(c); faces.Add(d); faces.Add(a); faces.Add(d); faces.Add(b); }
                }
            var mesh = new Mesh { name = "Natural Ocean: ring" };
            mesh.vertices = points;
            mesh.triangles = faces.ToArray();
            // (the shader puts the points where they go: nothing here says where the mesh is, so it is never left out of the picture)
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
            return mesh;
        }

        /// <summary>
        /// The rings laid round a point of the sea: `mid` that point in the scene, `first` and `second` the sea's two level
        /// axes there (with `up`), `at` where the point is on those axes (m, as the waves have it), `cell` the innermost
        /// ring's cell (m), `count` how many rings. Returns how far from the point the outermost ring's own rim is.
        /// </summary>
        public float Lay(Vector3 mid, Vector3 first, Vector3 up, Vector3 second, Vector2d at, float cell, int count)
        {
            count = Mathf.Clamp(count, 1, Rings);
            Quaternion turned = Quaternion.LookRotation(second, up);
            for (int n = 0; n < Rings; n++)
            {
                if (n >= count) { drawn[n].enabled = false; continue; }
                double size = cell * Math.Pow(2.0, n), step = 2.0 * size;
                double sx = Math.Round(at.x / step) * step - at.x, sz = Math.Round(at.y / step) * step - at.y;
                ring[n].position = mid + first * (float)sx + second * (float)sz;
                ring[n].rotation = turned;
                ring[n].localScale = new Vector3((float)size, (float)size, (float)size);
                drawn[n].enabled = true;
            }
            if (outer != count - 1)
            {
                if (outer > 0 && outer < Rings) shape[outer].sharedMesh = hollow;
                outer = count - 1;
                shape[outer].sharedMesh = outer == 0 ? whole : outermost;
            }
            return cell * Mathf.Pow(2f, count - 1) * Cells * 0.5f;
        }

        // (when a scene is left the game has already destroyed the rings by the time the sea is told: nothing to hide then)
        public void Hide() { for (int n = 0; n < Rings; n++) if (drawn[n] != null) drawn[n].enabled = false; }

        /// <summary>The innermost ring (the full square under the camera), and where it is laid this frame: for drawing it once more by hand (Sea.Lens).</summary>
        public Mesh Inner => whole;
        public Matrix4x4 InnerAt => ring[0].localToWorldMatrix;

        /// <summary>Whether the sea's mesh is being drawn.</summary>
        public bool Showing => drawn[0] != null && drawn[0].enabled;

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(whole); UnityEngine.Object.Destroy(hollow); UnityEngine.Object.Destroy(outermost);
            if (Material != null) UnityEngine.Object.Destroy(Material);
        }
    }
}
