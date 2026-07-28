using GBX.NET;
using GBX.NET.Engines.GameData;
using GBX.NET.Engines.Plug;
using System.Reflection;
using TM_GenericMapping.IO;
using TM_GenericMapping.Messaging;
using static GBX.NET.Engines.Plug.CPlugSurface;

namespace TM_GenericMapping.Items;

public class MeshExtractor
{

    public ToolResult<NormalizedMesh> ExtractMesh(CGameItemModel item)
    {
        ToolResult<NormalizedMesh> normalizedMeshResult = default;
        if (ItemExtensions.TryGetCrystal(item, out var crystal))
            normalizedMeshResult = ExtractFromCrystal(crystal);
        else if (ItemExtensions.TryGetDynaObjectModel(item, out var dynaModel))
            normalizedMeshResult = ExtractFromDynaModel(dynaModel);
        else if (ItemExtensions.TryGetStaticObjectModel(item, out var staticModel))
            normalizedMeshResult = ExtractFromStaticModel(staticModel);
        // needs to be last because could cover static object model
        else if (ItemExtensions.TryGetSolid2Model(item, out var solid2Model))
            normalizedMeshResult = ExtractFromSolid2Model(solid2Model);

        if (normalizedMeshResult.IsSuccess)
        {
            ToolResult<NormalizedSubmesh>? subMeshResult = null!;
            if (ItemExtensions.TryGetTriggerSpecial(item, out var triggerSpecial))
            {
                subMeshResult = ExtractFromTriggerSpecial(triggerSpecial);
            }
            else if (ItemExtensions.TryGetTriggerWaypoint(item, out var triggerWaypoint))
            {
                subMeshResult = ExtractFromTriggerWaypoint(triggerWaypoint);
            }
            else if (item.EntityModel is not null && 
                item.EntityModel is CGameCommonItemEntityModel commonItemEntityModel && 
                commonItemEntityModel.TriggerShape is not null &&
                commonItemEntityModel.TriggerShape is CPlugSurface surf)
            {
                subMeshResult = ExtractFromSurface(surf);
                subMeshResult.Value.Value.Type = SubmeshType.Trigger_Special;
            }
            if(subMeshResult.HasValue)
                normalizedMeshResult.Value.Submeshes = [.. normalizedMeshResult.Value.Submeshes, subMeshResult.Value.Value];

            normalizedMeshResult.Value.PlacementParam = item.DefaultPlacement;
            normalizedMeshResult.Value.IconWebP = item.IconWebP;
            normalizedMeshResult.Value.Icon = item.Icon;
            return normalizedMeshResult;
        }

        return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.UnsupportedMesh);
    }

    public ToolResult<NormalizedMesh> ExtractFromCrystal(CPlugCrystal crystal)
    {

        var submeshes = new List<NormalizedSubmesh>();

        foreach (var layer in crystal.Layers)
        {
            // Two-pass: group split vertices by material first, then concatenate
            // so each material produces a contiguous index range (NormalizedSubmesh)

            // per-material buckets of indices (into the shared vertex buffer)
            var buckets = new Dictionary<CPlugMaterialUserInst, (
                List<Vec3> positions,
                List<Vec3> normals,
                List<Vec2> texCoords,
                List<Vec2> lightmapCoords,
                List<int> indices,
                SubmeshType type,
                bool notCollidable,
                Dictionary<(Vec3, Vec2, Vec2), int> weldMap)>();

            SubmeshProperties properties = SubmeshProperties.None;

            switch (layer)
            {
                case CPlugCrystal.GeometryLayer geo:
                    {
                        var sourcePositions = geo.Crystal.Positions;
                        if(!geo.IsEnabled)
                            properties |= SubmeshProperties.Disabled;
                        if(!geo.IsVisible)
                            properties |= SubmeshProperties.Invisible;
                        if(!geo.Collidable)
                            properties |= SubmeshProperties.NonCollidable;
                        foreach (var face in geo.Crystal.Faces)
                        {
                            var mat = face.Material.MaterialUserInst;
                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), SubmeshType.Mesh, mat.SurfacePhysicId == CPlugSurface.MaterialId.NotCollidable, []);
                                buckets[mat] = bucket;
                            }

                            // fan triangulation — fully split vertices (per corner)
                            for (int i = 1; i < face.Vertices.Length - 1; i++)
                            {
                                var corners = new[] { face.Vertices[0], face.Vertices[i], face.Vertices[i + 1] };

                                foreach (var corner in corners)
                                {
                                    var key = (sourcePositions[corner.Index], corner.TexCoord, corner.LightmapCoord);
                                    if (!bucket.weldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.positions.Count;
                                        bucket.weldMap[key] = dst;
                                        bucket.positions.Add(sourcePositions[corner.Index]);
                                        bucket.texCoords.Add(corner.TexCoord);
                                        bucket.lightmapCoords.Add(corner.LightmapCoord);
                                        bucket.normals.Add(Vec3.Zero);
                                    }
                                    bucket.indices.Add(dst);
                                }
                            }
                        }
                    }
                    break;
                case CPlugCrystal.TriggerLayer trigger:
                    {
                        var sourcePositions = trigger.Crystal.Positions;
                        if (!trigger.IsEnabled)
                            properties |= SubmeshProperties.Disabled;
                        properties|= SubmeshProperties.Invisible | SubmeshProperties.NonCollidable;
                        foreach (var face in trigger.Crystal.Faces)
                        {
                            var mat = face.Material.MaterialUserInst;

                            if (!buckets.TryGetValue(mat, out var bucket))
                            {
                                bucket = (new(), new(), new(), new(), new(), SubmeshType.Trigger_Waypoint, true, []);
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
                                    var key = (sourcePositions[corner.Index], corner.TexCoord, corner.LightmapCoord);
                                    if (!bucket.weldMap.TryGetValue(key, out int dst))
                                    {
                                        dst = bucket.positions.Count;
                                        bucket.weldMap[key] = dst;
                                        bucket.positions.Add(sourcePositions[corner.Index]);
                                        bucket.texCoords.Add(corner.TexCoord);
                                        bucket.lightmapCoords.Add(corner.LightmapCoord);
                                        bucket.normals.Add(Vec3.Zero);
                                    }
                                    bucket.indices.Add(dst);
                                }
                            }
                        }
                    }
                    break;
                default:
                    continue;
            }

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
                    Type = bucket.type,
                    Properties = properties,
                    Name = MatToName(mat),
                });
            }
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

        var properties = SubmeshProperties.None;
        if(material.SurfacePhysicId == CPlugSurface.MaterialId.NotCollidable)
            properties |= SubmeshProperties.NonCollidable;
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
            Type = SubmeshType.Mesh,
            Properties = properties,
            Name = MatToName(material)
        };
        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }

    public ToolResult<NormalizedMesh> ExtractFromDynaModel(CPlugDynaObjectModel dynaObjectModel)
    {
        NormalizedMesh mesh = null!;
        if (dynaObjectModel.Mesh is not null)
        {
            var result = ExtractFromSolid2Model(dynaObjectModel.Mesh);
            if (result.IsFailure)
                ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);
            mesh = result.Value;
        }

        if (dynaObjectModel.StaticShape is not null)
        {
            var result = ExtractFromSurface(dynaObjectModel.StaticShape);
            if (result.IsFailure)
                return ToolResult.Fail(result);
            result.Value.Type = SubmeshType.Static_Shape;
            mesh.Submeshes = [.. mesh.Submeshes, result.Value];
        }
        if (dynaObjectModel.DynaShape is not null)
        {
            var result = ExtractFromSurface(dynaObjectModel.DynaShape);
            if (result.IsFailure)
                return ToolResult.Fail(result);
            result.Value.Type = SubmeshType.Dyna_Shape;
            mesh.Submeshes = [.. mesh.Submeshes, result.Value];
        }
    
        return ToolResult.Success(mesh, nameof(MeshExtractor));
    }
    public ToolResult<NormalizedMesh> ExtractFromStaticModel(CPlugStaticObjectModel staticObjectModel)
    {
        // surfaces (DynaShape/StaticShape) are intentionally ignored here —
        // they will be generated separately from NormalizedMesh when writing the item
        if (staticObjectModel.Mesh is not null)
        {
            var result = ExtractFromSolid2Model(staticObjectModel.Mesh);
            if(result.IsFailure)
                return result;
            if(!staticObjectModel.IsMeshCollidable)
                foreach(var submesh in result.Value.Submeshes)
                    submesh.Properties |= SubmeshProperties.NonCollidable;
            if (staticObjectModel.Shape == null)
                return result;
            var shapeResult = ExtractFromSurface(staticObjectModel.Shape);
            if (shapeResult.IsSuccess)
            {
                shapeResult.Value.Type = SubmeshType.Static_Shape;
                result.Value.Submeshes = [.. result.Value.Submeshes, shapeResult.Value];
            }
            return result;
        }
        else
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingMesh);
    }

    public ToolResult<NormalizedSubmesh> ExtractFromTriggerSpecial(NPlugTrigger_SSpecial triggerSpecial)
    {
        var triggerShape = triggerSpecial.GetTriggerShape();
        if(triggerShape == null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingTriggerShape);
        var result = ExtractFromSurface(triggerShape);
        if(result.IsFailure)
            return ToolResult.Fail(result);
        result.Value.Type = SubmeshType.Trigger_Special;
        return result;
    }
    public ToolResult<NormalizedSubmesh> ExtractFromTriggerWaypoint(NPlugTrigger_SWaypoint triggerWaypoint)
    {
        var triggerShape = triggerWaypoint.GetTriggerShape();
        if (triggerShape == null)
            return ToolResult.Fail(nameof(MeshExtractor), ErrorCodes.MeshExtractor.MissingTriggerShape);
        var result = ExtractFromSurface(triggerShape);
        if (result.IsFailure)
            return ToolResult.Fail(result);
        result.Value.Type = SubmeshType.Trigger_Waypoint;
        //TODO: waypoint type
        return result;
    }
    public ToolResult<NormalizedSubmesh> ExtractFromSurface(CPlugSurface surface)
    {
        var mesh = new NormalizedSubmesh();
        var surf = surface.Surf as CPlugSurface.Mesh;
        mesh.Positions = surf?.Vertices.ToArray();
        mesh.Indices = surf?.Triangles.SelectMany(t => new[] { t.Indices.X, t.Indices.Y, t.Indices.Z }).ToArray();
        mesh.Material = CreateErrorMat();
        mesh.SurfaceMaterialIds = surf.Triangles.Select(t => (MaterialId)t.U02).ToArray();
        mesh.Name = MatToName(mesh.Material);
        return ToolResult.Success(mesh, nameof(MeshExtractor));
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
    CPlugMaterialUserInst CreateErrorMat()
    {
        var mat = new CPlugMaterialUserInst()
        {
            MaterialName = "",
            Model = "",
            BaseTexture = "",
            
        };
        //{
        //    BaseTexture = "",
        //    Color = [],
        //    Csts = null,
        //    HidingGroup = "",
        //    IsNatural = false,
        //    IsUsingGameMaterial = false,
        //    Link = null,
        //    MaterialName = "",
        //    Model = "",
        //    SurfaceGameplayId = CPlugMaterialUserInst.GameplayId.None,
        //    SurfacePhysicId = CPlugSurface.MaterialId.Concrete,
        //    TextureSizeInMeters = 1,
        //    TilingU = CPlugMaterialUserInst.ETexAddress.Wrap,
        //    TilingV = CPlugMaterialUserInst.ETexAddress.Wrap,
        //    UserTextures = [],
        //};
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD000>(out var c1);
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD001>(out var c2);
        c2.U02 = 0;
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD002>(out var c3);
        return mat;
    }

    string MatToName(CPlugMaterialUserInst mat)
    {
        if(!string.IsNullOrWhiteSpace(mat.MaterialName))
            return mat.MaterialName;
        if (!string.IsNullOrWhiteSpace(mat.Link))
            return string.Join("\\", mat.Link.Split('\\').TakeLast(2));
        return "Unknown Material";
    }

    /// <summary>
    /// Welds vertices that share the same position, texCoord and lightmapCoord.
    /// Returns remapped positions/UVs and a new index buffer.
    /// </summary>
    (Vec3[] positions, Vec2[] texCoords, Vec2[] lightmapCoords, Vec3[] normals, int[] indices)
        WeldVertices(Vec3[] positions, Vec2[]? texCoords, Vec2[]? lightmapCoords, Vec3[]? normals, int[] indices)
    {
        var map = new Dictionary<(Vec3 pos, Vec2 uv, Vec2 lm), int>();
        var newPositions = new List<Vec3>();
        var newTexCoords = new List<Vec2>();
        var newLightmap = new List<Vec2>();
        var newNormals = new List<Vec3>();
        var newIndices = new int[indices.Length];

        for (int i = 0; i < indices.Length; i++)
        {
            int src = indices[i];
            var key = (
                positions[src],
                texCoords?[src] ?? default,
                lightmapCoords?[src] ?? default
            );

            if (!map.TryGetValue(key, out int dst))
            {
                dst = newPositions.Count;
                map[key] = dst;
                newPositions.Add(positions[src]);
                newTexCoords.Add(texCoords?[src] ?? default);
                newLightmap.Add(lightmapCoords?[src] ?? default);
                newNormals.Add(normals?[src] ?? Vec3.Zero);
            }
            newIndices[i] = dst;
        }

        return (
            [.. newPositions],
            [.. newTexCoords],
            [.. newLightmap],
            [.. newNormals],
            newIndices
        );
    }

}
