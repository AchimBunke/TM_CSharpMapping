using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
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
            return normalizedMeshResult;
        }

        return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedMesh);
    }

    ToolResult<NormalizedMesh> ExtractFromCrystal(CPlugCrystal crystal)
    {
        // Two-pass: group split vertices by material first, then concatenate
        // so each material produces a contiguous index range (NormalizedSubmesh)

        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var uv0 = new List<Vec2>();
        var uv1 = new List<Vec2>();

        // per-material buckets of indices (into the shared vertex buffer)
        var buckets = new Dictionary<CPlugMaterialUserInst, List<int>>();

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
                    bucket = new List<int>();
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
                        bucket.Add(positions.Count);
                        positions.Add(sourcePositions[corner.Index]);
                        uv0.Add(corner.TexCoord);
                        uv1.Add(corner.LightmapCoord);
                        normals.Add(Vec3.Zero); // filled below
                    }
                }
            }
        }

        // concatenate buckets into final index buffer, recording submesh ranges
        var indices = new List<int>();
        var submeshes = new List<NormalizedSubmesh>();

        foreach (var (mat, bucket) in buckets)
        {
            submeshes.Add(new NormalizedSubmesh
            {
                IndexStart = indices.Count,
                IndexCount = bucket.Count,
                Material = mat
            });
            indices.AddRange(bucket);
        }

        var posArr = positions.ToArray();
        var idxArr = indices.ToArray();

        var nrmArr = ComputeSmoothNormals(positions.ToArray(), indices.ToArray());

        var mesh = new NormalizedMesh
        {
            Positions = posArr,
            Normals = nrmArr,
            TexCoords = uv0.ToArray(),
            LightmapCoords = uv1.ToArray(),
            Indices = idxArr,
            Submeshes = submeshes.ToArray(),
            SourceData = crystal
        };
        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }

    ToolResult<NormalizedMesh> ExtractFromSolid2Model(CPlugSolid2Model solid2Model)
    {
        var positions = new List<Vec3>();
        var normals = new List<Vec3>();
        var uv0 = new List<Vec2>();
        var uv1 = new List<Vec2>();
        var indices = new List<int>();
        var submeshes = new List<NormalizedSubmesh>();

        foreach (var shaded in solid2Model.ShadedGeoms)
        {
            var visual = solid2Model.Visuals[shaded.VisualIndex];
            if (visual is not CPlugVisualIndexedTriangles vit)
                continue;

            int indexStart = indices.Count;
            int vertOffset = positions.Count;

            var vertexStream = visual.VertexStreams[0];
            positions.AddRange(vertexStream.Positions);
            normals.AddRange(vertexStream.Normals);
            if(vertexStream.UVs.Count > 0)
                uv0.AddRange(vertexStream.UVs[0]);
            if(vertexStream.UVs.Count > 1)
                uv1.AddRange(vertexStream.UVs[1]);

            foreach (var idx in vit.IndexBuffer.Indices)
                indices.Add(vertOffset + idx);

            submeshes.Add(new NormalizedSubmesh
            {
                IndexStart = indexStart,
                IndexCount = indices.Count - indexStart,
                Material = solid2Model.CustomMaterials[shaded.MaterialIndex].MaterialUserInst
            });
        }

        return ToolResult.Success(new NormalizedMesh
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            TexCoords = uv0.ToArray(),
            LightmapCoords = uv1.ToArray(),
            Indices = indices.ToArray(),
            Submeshes = submeshes.ToArray(),
            SourceData = solid2Model
        }, nameof(MeshExtractor));
    }
    ToolResult<NormalizedMesh> ExtractFromDynaModel(CPlugDynaObjectModel dynaObjectModel)
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
}
