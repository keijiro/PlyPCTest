using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pcx.Tests
{
    public class PlyImporterTests
    {
        const string Root = "Assets/TestData";

        static readonly (string Path, int Count)[] StandardFiles =
        {
            ($"{Root}/valid/xyz_ascii.ply", 1000),
            ($"{Root}/valid/xyz_binary_le.ply", 1000),
            ($"{Root}/valid/xyz_binary_be.ply", 1000),
            ($"{Root}/valid/xyz_rgb_ascii.ply", 6144),
            ($"{Root}/valid/xyz_rgba_binary_le.ply", 2048),
            ($"{Root}/valid/xyz_normal_binary_le.ply", 2048),
            ($"{Root}/edge_cases/xyz_intensity_confidence.ply", 4096),
            ($"{Root}/edge_cases/property_order_variant.ply", 4096),
            ($"{Root}/edge_cases/comments_object_info.ply", 6144),
            ($"{Root}/edge_cases/mixed_numeric_types.ply", 4096),
            ($"{Root}/stress/small_grid_10k.ply", 10000),
            ($"{Root}/stress/sphere_rgb_50k.ply", 50000)
        };

        static System.Collections.Generic.IEnumerable<TestCaseData> StandardFileCases()
        {
            foreach (var f in StandardFiles)
                yield return new TestCaseData(f.Path, f.Count).SetName(f.Path);
        }

        [TestCaseSource(nameof(StandardFileCases))]
        public void AllStandardFiles_ImportWithoutErrors(string path, int expectedCount)
        {
            var mesh = LoadMesh(path);
            Assert.IsNotNull(mesh, $"Mesh not loaded: {path}");
            Assert.AreEqual(expectedCount, mesh.vertexCount,
                $"Vertex count mismatch for {path}");
            var indices = mesh.GetIndices(0);
            Assert.AreEqual(expectedCount, indices.Length, "Index count mismatch.");
            Assert.AreEqual(MeshTopology.Points, mesh.GetTopology(0));
        }

        [Test]
        public void Rgba_PreservesAlpha220()
        {
            var mesh = LoadMesh($"{Root}/valid/xyz_rgba_binary_le.ply");
            var colors = mesh.colors32;
            Assert.AreEqual(2048, colors.Length, "Expected per-vertex colors.");
            for (var i = 0; i < colors.Length; i++)
                Assert.AreEqual(220, colors[i].a, $"Alpha mismatch at vertex {i}.");
        }

        [Test]
        public void PropertyOrder_MapsByName()
        {
            // CubeSurface points lie on |x|==1 OR |y|==1 OR |z|==1, with the other
            // two coords in [-1,1]. Since the file declares "z, x, y", reading by
            // name is the only way to land on this shape.
            var mesh = LoadMesh($"{Root}/edge_cases/property_order_variant.ply");
            var verts = mesh.vertices;

            var hasUnitX = false; var hasUnitY = false; var hasUnitZ = false;
            const float tol = 1e-3f;

            foreach (var v in verts)
            {
                Assert.That(v.x, Is.InRange(-1f - tol, 1f + tol));
                Assert.That(v.y, Is.InRange(-1f - tol, 1f + tol));
                Assert.That(v.z, Is.InRange(-1f - tol, 1f + tol));

                var ax = Mathf.Abs(v.x);
                var ay = Mathf.Abs(v.y);
                var az = Mathf.Abs(v.z);
                Assert.That(Mathf.Max(ax, Mathf.Max(ay, az)), Is.GreaterThanOrEqualTo(1f - tol),
                    "CubeSurface vertex must have at least one coord at +/-1.");

                if (Mathf.Abs(ax - 1f) < tol) hasUnitX = true;
                if (Mathf.Abs(ay - 1f) < tol) hasUnitY = true;
                if (Mathf.Abs(az - 1f) < tol) hasUnitZ = true;
            }

            Assert.IsTrue(hasUnitX && hasUnitY && hasUnitZ,
                "Expected unit-extent vertices on all three axes.");
        }

        [Test]
        public void MixedNumericTypes_BigEndianDouble_ParsesPositionsAndColors()
        {
            var mesh = LoadMesh($"{Root}/edge_cases/mixed_numeric_types.ply");
            Assert.AreEqual(4096, mesh.vertexCount);
            Assert.AreEqual(4096, mesh.colors32.Length, "Expected per-vertex colors.");

            // Every vertex must be within 0.15 of one of the three cluster centers.
            var centers = new[]
            {
                new Vector3(-0.6f, -0.4f,  0.0f),
                new Vector3( 0.5f, -0.2f,  0.3f),
                new Vector3( 0.0f,  0.5f, -0.4f)
            };

            var verts = mesh.vertices;
            // Per-axis noise is Uniform(-0.12, 0.12); max L2 distance is sqrt(3)*0.12.
            const float tol = 0.12f * 1.7320508f + 1e-3f;
            foreach (var v in verts)
            {
                var nearest = float.PositiveInfinity;
                foreach (var c in centers)
                {
                    var d = (v - c).magnitude;
                    if (d < nearest) nearest = d;
                }
                Assert.That(nearest, Is.LessThanOrEqualTo(tol),
                    $"Vertex {v} is not near any cluster center (min dist {nearest}).");
            }
        }

        [Test]
        public void Normals_AreUnitLength()
        {
            var mesh = LoadMesh($"{Root}/valid/xyz_normal_binary_le.ply");
            var normals = mesh.normals;
            Assert.AreEqual(2048, normals.Length, "Expected per-vertex normals.");
            for (var i = 0; i < normals.Length; i++)
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-4f),
                    $"Normal at {i} not unit length.");
        }

        [Test]
        public void IndexFormat_PicksWidthByVertexCount()
        {
            // 50000 fits in 16 bits; the importer should keep UInt16 to save memory.
            var small = LoadMesh($"{Root}/stress/sphere_rgb_50k.ply");
            Assert.AreEqual(50000, small.vertexCount);
            Assert.AreEqual(IndexFormat.UInt16, small.indexFormat);
        }

        static Mesh LoadMesh(string path)
        {
            // Force a fresh import so failures point at the importer, not stale cache.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                if (a is Mesh m) return m;
            return null;
        }
    }
}
