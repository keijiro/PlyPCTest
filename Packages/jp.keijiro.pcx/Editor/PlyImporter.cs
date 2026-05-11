using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pcx.Editor
{
    [ScriptedImporter(1, "ply")]
    public sealed class PlyImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            using var stream = File.OpenRead(ctx.assetPath);
            var header = PlyHeader.Read(stream);
            var body = PlyBodyReader.Read(stream, header);

            var name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            var mesh = BuildMesh(body);
            mesh.name = name;

            var material = new Material(Shader.Find("Pcx/PointCloudUnlit"))
            {
                name = name + " Material"
            };

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            ctx.AddObjectToAsset("prefab", go);
            ctx.AddObjectToAsset("mesh", mesh);
            ctx.AddObjectToAsset("material", material);
            ctx.SetMainObject(go);
        }

        static Mesh BuildMesh(PlyBody body)
        {
            var mesh = new Mesh
            {
                indexFormat = body.Positions.Length > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };

            mesh.SetVertices(body.Positions);
            if (body.Colors != null) mesh.SetColors(body.Colors);
            if (body.Normals != null) mesh.SetNormals(body.Normals);

            var indices = new int[body.Positions.Length];
            for (var i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.SetIndices(indices, MeshTopology.Points, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
