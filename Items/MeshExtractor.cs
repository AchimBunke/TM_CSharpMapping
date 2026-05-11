using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Reflection;
using TM_GenericMapping.Messaging;

namespace TM_GenericMapping.Items;

public class MeshExtractor
{

    public ToolResult<NormalizedMesh> ExtractMesh(CGameItemModel item)
    {
        ToolResult<NormalizedMesh> normalizedMeshResult = default;
        if (ItemExtensions.TryGetCrystal(item, out var crystal))
            normalizedMeshResult = ExtractFromCrystal(crystal);
        else if (ItemExtensions.TryGetSolid2Model(item, out var solid2Model))
            normalizedMeshResult = ExtractFromSolid2Model(solid2Model);
        else if (ItemExtensions.TryGetDynaObjectModel(item, out var dynaModel))
            normalizedMeshResult = ExtractFromDynaModel(dynaModel);

        if(normalizedMeshResult.IsSuccess)
        {
            normalizedMeshResult.Value.PlacementParam = item.DefaultPlacement;
            normalizedMeshResult.Value.IconWebP = item.IconWebP;
            normalizedMeshResult.Value.Icon = item.Icon;
            return normalizedMeshResult;
        }

        return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedMesh);
    }

    public ToolResult<NormalizedMesh> ExtractFromCrystal(CPlugCrystal crystal)
    {
        // Two-pass: group split vertices by material first, then concatenate
        // so each material produces a contiguous index range (NormalizedSubmesh)

        // per-material buckets of indices (into the shared vertex buffer)
        var buckets = new Dictionary<CPlugMaterialUserInst, (
            List<Vec3> positions,
            List<Vec3> normals,
            List<Vec2> texCoords,
            List<Vec2> lightmapCoords,
            List<int> indices)>();

        foreach (var layer in crystal.Layers)
        {
            if (layer is not CPlugCrystal.GeometryLayer geo)
                continue;
            var sourcePositions = geo.Crystal.Positions;

            foreach (var face in geo.Crystal.Faces)
            {
                var mat = face.Material.MaterialUserInst;

                if (!buckets.TryGetValue(mat, out var bucket))
                {
                    bucket = (new(), new(), new(), new(), new());
                    buckets[mat] = bucket;
                }

                // fan triangulation — fully split vertices (per corner)
                for (int i = 1; i < face.Vertices.Length - 1; i++)
                {
                    var corners = new[]
                    {
                        face.Vertices[0],
                        face.Vertices[i],
                        face.Vertices[i + 1]
                    };

                    foreach (var corner in corners)
                    {
                        bucket.indices.Add(bucket.positions.Count);
                        bucket.positions.Add(sourcePositions[corner.Index]);
                        bucket.texCoords.Add(corner.TexCoord);
                        bucket.lightmapCoords.Add(corner.LightmapCoord);
                        bucket.normals.Add(Vec3.Zero); // computed below
                    }
                }
            }
        }

        var submeshes = new List<NormalizedSubmesh>();

        // concatenate buckets into final index buffer, recording submesh ranges
        var indices = new List<int>();


        foreach (var (mat, bucket) in buckets)
        {
            var posArr = bucket.positions.ToArray();
            var idxArr = bucket.indices.ToArray();
            var nrmArr = ComputeSmoothNormals(posArr, idxArr);

            submeshes.Add(new NormalizedSubmesh
            {
                Positions = posArr,
                Normals = nrmArr,
                TexCoords = bucket.texCoords.Count > 0 ? bucket.texCoords.ToArray() : null,
                LightmapCoords = bucket.lightmapCoords.Count > 0 ? bucket.lightmapCoords.ToArray() : null,
                Colors = null, // crystal has no vertex colors
                Indices = idxArr,
                Material = mat,
            });
        }


        return ToolResult.Success(new NormalizedMesh
        {
            Submeshes = submeshes.ToArray(),
            SourceData = crystal
        }, nameof(MeshExtractor));
    }

    public ToolResult<NormalizedMesh> ExtractFromSolid2Model(CPlugSolid2Model solid2Model)
    {
        var submeshes = new List<NormalizedSubmesh>();

        foreach (var shaded in solid2Model.ShadedGeoms)
        {
            var visual = solid2Model.Visuals[shaded.VisualIndex];
            if (visual is not CPlugVisualIndexedTriangles vit)
                continue;
            
            var subMeshResult = ExtractFromVisual(vit, solid2Model.CustomMaterials[shaded.MaterialIndex].MaterialUserInst);
            if (subMeshResult.IsFailure)
                return ToolResult.Fail(subMeshResult);

            submeshes.Add(subMeshResult.Value);
        }

        return ToolResult.Success(new NormalizedMesh
        {
            Submeshes = submeshes.ToArray(),
            SourceData = solid2Model
        }, nameof(MeshExtractor));
    }

    public ToolResult<NormalizedSubmesh> ExtractFromVisual(CPlugVisualIndexedTriangles visual, CPlugMaterialUserInst material)
    {
        var stream = visual.VertexStreams[0];

        var tangentUsField = typeof(CPlugVertexStream).GetField("tangentUs",
          BindingFlags.NonPublic | BindingFlags.Instance);
        var tangentsUs = (Vec3[])tangentUsField?.GetValue(stream);

        var tangentVsField = typeof(CPlugVertexStream).GetField("tangentVs",
          BindingFlags.NonPublic | BindingFlags.Instance);
        var tangentVs = (Vec3[])tangentVsField?.GetValue(stream);

        var mesh = new NormalizedSubmesh
        {
            Positions = stream.Positions,
            Normals = stream.Normals,
            TexCoords = stream.UVs.TryGetValue(0, out var uv0) ? uv0 : null,
            LightmapCoords = stream.UVs.TryGetValue(1, out var uv1) ? uv1 : null,
            Colors = stream.Colors.TryGetValue(0, out var col) ? col : null,
            Indices = visual.IndexBuffer.Indices,
            Material = material,
            TangentUs = tangentsUs,
            TangentVs = tangentVs,
        };
        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }

    public ToolResult<NormalizedMesh> ExtractFromDynaModel(CPlugDynaObjectModel dynaObjectModel)
    {
        // surfaces (DynaShape/StaticShape) are intentionally ignored here —
        // they will be generated separately from NormalizedMesh when writing the item

        if (dynaObjectModel.Mesh is not null)
            return ExtractFromSolid2Model(dynaObjectModel.Mesh);
        return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);
    }

    static Vec3[] ComputeSmoothNormals(Vec3[] positions, int[] indices)
    {
        var normals = new Vec3[positions.Length];

        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];

            // weighted by triangle area (cross product magnitude = 2x area)
            var faceNormal = Vec3.GetCrossProduct(b - a, c - a);

            normals[indices[i]] += faceNormal;
            normals[indices[i + 1]] += faceNormal;
            normals[indices[i + 2]] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
            if (normals[i] != Vec3.Zero)
                normals[i] = normals[i].GetNormalized();

        return normals;
    }
    static (Vec3[] tangentUs, Vec3[] tangentVs) ComputeTangents(
    Vec3[] positions, Vec3[] normals, Vec2[] uvs, int[] indices)
    {
        var tangents = new Vec3[positions.Length];
        var bitangents = new Vec3[positions.Length];

        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];

            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var deltaUV1 = uvs[i1] - uvs[i0];
            var deltaUV2 = uvs[i2] - uvs[i0];

            float denom = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (MathF.Abs(denom) < 1e-6f) continue; // degenerate UV
            float r = 1f / denom;

            var tangent = (edge1 * deltaUV2.Y - edge2 * deltaUV1.Y) * r;
            var bitangent = (edge2 * deltaUV1.X - edge1 * deltaUV2.X) * r;

            tangents[i0] += tangent; tangents[i1] += tangent; tangents[i2] += tangent;
            bitangents[i0] += bitangent; bitangents[i1] += bitangent; bitangents[i2] += bitangent;
        }

        // orthogonalize against normal (Gram-Schmidt)
        for (int i = 0; i < positions.Length; i++)
        {
            var n = normals[i];
            var t = tangents[i];

            tangents[i] = (t - n * Vec3.GetDotProduct(n, t)).GetNormalized();
            bitangents[i] = (bitangents[i]).GetNormalized();
        }

        return (tangents, bitangents);
    }
}
