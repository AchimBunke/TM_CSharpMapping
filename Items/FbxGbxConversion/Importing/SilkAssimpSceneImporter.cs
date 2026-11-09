
using GBX.NET;
using GBX.NET.Engines.Meta;
using Silk.NET.Assimp;
using Silk.NET.Core;
using System.Drawing.Drawing2D;
using System.Net.Mail;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.FbxGbxConversion.Serialization;
using TM_GenericMapping.Items.MeshCompilation;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace TM_GenericMapping.Items.FbxGbxConversion.Importing;

public class SilkAssimpSceneImporter : ISceneImporter
{
    // =====================================================================
    //  COORDINATE SPACE CONFIGURATION
    //  This block is the ONLY place you need to touch to change the target
    //  axes. Everything below (meshes, nodes, lights, FixConfigAxes) derives
    //  from it.
    // =====================================================================

    // Where each Blender axis ends up in the target (TM editor) space.
    static readonly Vector3 BlenderXInTarget = new(1f, 0f, 0f);   // Blender +X
    static readonly Vector3 BlenderYInTarget = new(0f, 0f, -1f);  // Blender +Y (forward)
    static readonly Vector3 BlenderZInTarget = new(0f, 1f, 0f);   // Blender +Z (up)

    // Row-vector matrix (System.Numerics):  v_target = v_blender * BlenderToTarget
    static readonly Matrix4x4 BlenderToTarget = new(
        BlenderXInTarget.X, BlenderXInTarget.Y, BlenderXInTarget.Z, 0f,
        BlenderYInTarget.X, BlenderYInTarget.Y, BlenderYInTarget.Z, 0f,
        BlenderZInTarget.X, BlenderZInTarget.Y, BlenderZInTarget.Z, 0f,
        0f, 0f, 0f, 1f);

    // Pure axis permutation/sign matrix -> inverse == transpose.
    static readonly Matrix4x4 TargetToBlender = Matrix4x4.Transpose(BlenderToTarget);

    // If you ever pick a mirrored basis (det < 0) triangle winding must be flipped.
    static readonly bool TargetMirrorsHandedness = BlenderToTarget.GetDeterminant() < 0f;

    // ---------------------------------------------------------------------
    //  What Blender's FBX exporter bakes into every top-level object of the
    //  file (NOT configurable by you, it's a property of the source files):
    //  axis conversion (forward -Z, up Y) and units in cm (x100).
    //  This is deliberately independent of BlenderToTarget: changing your
    //  target axes must not change how the file is "un-baked".
    // ---------------------------------------------------------------------
    const float FileUnitsPerMeter = 100f;

    static readonly Matrix4x4 FbxExporterAxes = new(
        1f, 0f, 0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    // file world = blenderWorld * FileBake   ->   blenderWorld = file world * FileBakeInverse
    static readonly Matrix4x4 FileBakeInverse =
        Matrix4x4.Transpose(FbxExporterAxes) * Matrix4x4.CreateScale(1f / FileUnitsPerMeter);

    // =====================================================================

    private readonly float _scale;

    public SilkAssimpSceneImporter(float scale)
    {
        _scale = scale;
    }

    public ImportedScene Import(Stream stream)
    {
        using var context = Assimp.GetApi();

        unsafe
        {
            var store = context.CreatePropertyStore();

            try
            {
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);

                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                byte[] data = memory.ToArray();

                fixed (byte* ptr = data)
                {
                    PostProcessSteps postProcess =
                        PostProcessSteps.Triangulate |
                        PostProcessSteps.CalculateTangentSpace;

                    Scene* scene = context.ImportFileFromMemoryWithProperties(
                        ptr,
                        (uint)data.Length,
                        (uint)postProcess,
                        "fbx", store);

                    if (scene == null)
                    {
                        throw new InvalidOperationException(
                            $"Assimp failed: {context.GetErrorStringS()}");
                    }

                    try
                    {
                        var importedScene = Convert(context, scene);

                        ApplyCoordinateSpaceFixup(importedScene);

                        return importedScene;
                    }
                    finally
                    {
                        context.ReleaseImport(scene);
                    }
                }
            }
            finally
            {
                context.ReleasePropertyStore(store);
            }
        }
    }

    unsafe object? ExtractMetadataValue(MetadataType type, void* valuePtr)
    {
        switch (type)
        {
            case MetadataType.Bool:
                return *(bool*)valuePtr;
            case MetadataType.Int32:
                return *(int*)valuePtr;
            case MetadataType.Int64:
                return *(long*)valuePtr;
            case MetadataType.Uint32:
                return *(uint*)valuePtr;
            case MetadataType.Uint64:
                return *(ulong*)valuePtr;
            case MetadataType.Float:
                return *(float*)valuePtr;
            case MetadataType.Double:
                return *(double*)valuePtr;
            case MetadataType.Aistring:
                {
                    var str = (AssimpString*)valuePtr;
                    return str->ToString();
                }
            case MetadataType.Aivector3D:
                {
                    var vec = (Vector3*)valuePtr;
                    return new Vector3(vec->X, vec->Y, vec->Z);
                }
            case MetadataType.Aimetadata:
                return ExtractMetadataValue((Metadata*)valuePtr);
            case MetadataType.MetaMax:
                return null;
            default:
                throw new NotSupportedException($"Unsupported metadata type: {type}");
        }
    }

    unsafe Dictionary<string, object?> ExtractMetadataValue(Metadata* metaData)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (metaData is null)
            return result;
        for (uint i = 0; i < metaData->MNumProperties; i++)
        {
            var key = metaData->MKeys[i];
            var entry = metaData->MValues[i];
            var data = ExtractMetadataValue(entry.MType, entry.MData);

            result[key] = data;
        }

        return result;
    }

    unsafe ImportedScene Convert(Assimp assimp, Scene* scene)
    {
        if (scene == null)
            throw new ArgumentNullException(nameof(scene));

        var importedScene = new ImportedScene();

        // Scene metadata
        var metadata = ExtractMetadataValue(scene->MMetaData);

        // Meshes
        for (uint i = 0; i < scene->MNumMeshes; i++)
        {
            importedScene.Meshes.Add(
                ConvertMesh(scene->MMeshes[i]));
        }

        // Materials
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            importedScene.Materials.Add(
                ConvertMaterial(assimp, scene->MMaterials[i]));
        }

        // Root node
        if (scene->MRootNode != null)
        {
            importedScene.RootNode =
                ConvertNode(
                    scene->MRootNode,
                    null,
                    Matrix4x4.Identity);
        }

        // Lights
        if (scene->MNumLights > 0 && scene->MLights != null)
        {
            var nodesByName = BuildNodeLookup(importedScene);

            for (uint i = 0; i < scene->MNumLights; i++)
            {
                Light* light = scene->MLights[i];

                string name = light->MName.ToString();

                if (!nodesByName.TryGetValue(name, out var node))
                    continue;

                importedScene.Lights.Add(new ImportedLight
                {
                    NodeName = name,

                    Type = light->MType switch
                    {
                        LightSourceType.Directional =>
                            ImportedLightType.Directional,

                        LightSourceType.Point =>
                            ImportedLightType.Point,

                        LightSourceType.Spot =>
                            ImportedLightType.Spot,

                        LightSourceType.Area =>
                            ImportedLightType.Area,

                        _ => ImportedLightType.Point
                    },

                    GlobalTransform = node.GlobalTransform
                });
            }
        }

        return importedScene;
    }

    unsafe ImportedNode ConvertNode(Node* node, ImportedNode? parent, Matrix4x4 parentGlobal)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        var metadata = ExtractMetadataValue(node->MMetaData);

        // Assimp: row-major, column vectors. Transpose -> System.Numerics: row vectors.
        Matrix4x4 local = Matrix4x4.Transpose(node->MTransformation);

        // Row-vector convention: v * local * parent * grandparent ...
        // (the old `parentGlobal * local` was the column-vector order and only
        //  happened to work while the parent was the identity.)
        Matrix4x4 global = local * parentGlobal;

        var importedNode = new ImportedNode
        {
            Name = node->MName.ToString(),
            LocalTransform = local,
            GlobalTransform = global,
            Parent = parent
        };

        for (uint i = 0; i < node->MNumMeshes; i++)
            importedNode.MeshIndices.Add(checked((int)node->MMeshes[i]));

        for (uint i = 0; i < node->MNumChildren; i++)
            importedNode.Children.Add(ConvertNode(node->MChildren[i], importedNode, global));

        return importedNode;
    }

    unsafe ImportedMesh ConvertMesh(Mesh* mesh)
    {
        if (mesh == null)
            throw new ArgumentNullException(nameof(mesh));

        int vertexCount = checked((int)mesh->MNumVertices);

        var positions = new Vector3[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            var v = mesh->MVertices[i];

            positions[i] = new Vector3(
                v.X,
                v.Y,
                v.Z);
        }

        // Normals
        Vector3[] normals;

        if (mesh->MNormals != null)
        {
            normals = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                var v = mesh->MNormals[i];

                normals[i] = new Vector3(
                    v.X,
                    v.Y,
                    v.Z);
            }
        }
        else
        {
            normals = Array.Empty<Vector3>();
        }

        // Tangents / bitangents
        Vector3[]? tangents = null;
        Vector3[]? bitangents = null;

        if (mesh->MTangents != null && mesh->MBitangents != null)
        {
            tangents = new Vector3[vertexCount];
            bitangents = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                var t = mesh->MTangents[i];
                var b = mesh->MBitangents[i];

                tangents[i] = new Vector3(t.X, t.Y, t.Z);
                bitangents[i] = new Vector3(b.X, b.Y, b.Z);
            }
        }

        // Texture coordinates
        //
        // Assimp has a fixed number of texture coordinate channels.
        // MTextureCoords[c] == null means that channel doesn't exist.
        int numTexChannels = 0;
        while (numTexChannels < 8 && mesh->MTextureCoords[numTexChannels] != null)
            numTexChannels++;

        var texChannels = new Vector2[numTexChannels][];

        for (int c = 0; c < numTexChannels; c++)
        {
            var src = mesh->MTextureCoords[c];
            var channel = new Vector2[vertexCount];

            for (int i = 0; i < vertexCount; i++)
                channel[i] = new Vector2(src[i].X, src[i].Y);

            texChannels[c] = channel;
        }

        // Vertex colors
        int numColorChannels = 0;
        while (numColorChannels < 8 && mesh->MColors[numColorChannels] != null)
            numColorChannels++;

        var colorChannels = new Vector4[numColorChannels][];

        for (int c = 0; c < numColorChannels; c++)
        {
            var src = mesh->MColors[c];
            var channel = new Vector4[vertexCount];

            for (int i = 0; i < vertexCount; i++)
                channel[i] = new Vector4(src[i].X, src[i].Y, src[i].Z, src[i].W);

            colorChannels[c] = channel;
        }

        // Faces / indices
        //
        // With PostProcessSteps.Triangulate every face should have
        // exactly 3 indices.
        var indices = new int[checked((int)mesh->MNumFaces * 3)];

        int index = 0;

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            Face* face = &mesh->MFaces[i];

            if (face->MNumIndices != 3)
                throw new InvalidOperationException(
                    $"Expected triangle, got {face->MNumIndices} indices.");

            indices[index++] = checked((int)face->MIndices[0]);
            indices[index++] = checked((int)face->MIndices[1]);
            indices[index++] = checked((int)face->MIndices[2]);
        }

        return new ImportedMesh
        {
            Name = mesh->MName.ToString(),

            MaterialIndex =
                checked((int)mesh->MMaterialIndex),

            Positions = positions,
            Normals = normals,

            Tangents = tangents,
            BiTangents = bitangents,

            TextureCoordinateChannels = texChannels,
            VertexColorChannels = colorChannels,

            Indices = indices
        };
    }

    unsafe ImportedMaterial ConvertMaterial(Assimp assimp, Material* material)
    {
        if (material == null)
            throw new ArgumentNullException(nameof(material));

        return new ImportedMaterial
        {
            Name = GetMaterialName(assimp, material),
        };
    }

    unsafe string GetMaterialName(Assimp assimp, Material* material)
    {
        AssimpString name;

        Return result = assimp.GetMaterialString(
            material,
            Assimp.MaterialNameBase, // this is "?mat.name" (AI_MATKEY_NAME)
            0,                        // texture type (0 for name key)
            0,                        // index (0 for name key)
            &name);

        if (result == Return.Success)
        {
            // AssimpString.Data is a fixed byte buffer; Length tells you how many bytes are valid
            return System.Text.Encoding.UTF8.GetString(name.Data, (int)name.Length);
        }

        return string.Empty;
    }

    // =====================================================================
    //  Coordinate space fixup
    // =====================================================================
    //
    //  Model of the source file (Blender FBX exporter, default settings):
    //
    //      file world  =  blenderWorld * FileBake        (row vectors)
    //
    //  i.e. mesh vertices are untouched Blender-local data (Blender axes, meters)
    //  and the axis conversion + cm scale is baked into the top-level objects'
    //  transforms, so it ends up in the world matrix of EVERY node below them.
    //
    //  Target:
    //      - mesh data carries the axis conversion:  v' = v * C * scale
    //      - nodes carry no conversion at all; an unrotated Blender object ends up
    //        with an identity rotation, and a rotation about Blender's up axis
    //        becomes a rotation about the target's up axis:
    //
    //            worldTarget = C^-1 * blenderWorld * C      (translation * scale)
    //
    //  Working on WORLD matrices makes this independent of where the exporter put
    //  the baked conversion; locals are then re-derived from the new worlds, so
    //  any hierarchy depth works.
    //
    //  Assumes the Assimp root node itself is the identity.

    void ApplyCoordinateSpaceFixup(ImportedScene scene)
    {
        // 1) Mesh data: Blender-local -> target-local (+ import scale)
        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Length; i++)
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], BlenderToTarget) * _scale;

            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.Normals[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[i], BlenderToTarget));

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Length; i++)
                    mesh.Tangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Tangents[i], BlenderToTarget));

            if (mesh.BiTangents is not null)
                for (int i = 0; i < mesh.BiTangents.Length; i++)
                    mesh.BiTangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.BiTangents[i], BlenderToTarget));

            if (TargetMirrorsHandedness)
                FlipWindingInPlace(mesh.Indices);
        }

        // 2) Nodes (top-down, so the parent's NEW global is known when we do a child)
        if (scene.RootNode != null)
        {
            foreach (var child in scene.RootNode.Children)
                FixupNode(child, Matrix4x4.Identity);
        }

        // 3) Lights follow their (already fixed) node
        var nodesByName = BuildNodeLookup(scene);
        foreach (var light in scene.Lights)
        {
            if (nodesByName.TryGetValue(light.NodeName, out var node))
                light.GlobalTransform = LightGlobalFromNode(node.GlobalTransform);
        }
    }

    /// <summary>
    /// node.GlobalTransform is still in FILE space when this is called for a node
    /// (nodes are processed parent-first and each node only overwrites itself).
    /// </summary>
    void FixupNode(ImportedNode node, Matrix4x4 parentTargetGlobal)
    {
        Matrix4x4 targetGlobal = Snap(FileGlobalToTarget(node.GlobalTransform));

        if (!Matrix4x4.Invert(parentTargetGlobal, out Matrix4x4 parentInverse))
            throw new InvalidOperationException(
                $"Cannot import node '{node.Name}': a parent has a non-invertible transform (zero scale?).");

        // row vectors: global = local * parentGlobal  =>  local = global * parentGlobal^-1
        node.LocalTransform = Snap(targetGlobal * parentInverse);
        node.GlobalTransform = targetGlobal;

        foreach (var child in node.Children)
            FixupNode(child, targetGlobal);
    }

    /// <summary>File-space world matrix (cm, baked exporter axes) -> target-space world matrix (m).</summary>
    Matrix4x4 FileGlobalToTarget(Matrix4x4 fileGlobal)
    {
        // un-bake the exporter: file world -> Blender world (meters, Blender axes)
        Matrix4x4 blenderWorld = fileGlobal * FileBakeInverse;

        // conjugate into target axes (row vectors: C^-1 * M * C)
        Matrix4x4 target = TargetToBlender * blenderWorld * BlenderToTarget;

        // import scale only affects positions; mesh vertices are scaled separately,
        // node linear parts stay untouched so authored node scale is preserved.
        target.M41 *= _scale;
        target.M42 *= _scale;
        target.M43 *= _scale;
        target.M44 = 1f;

        return target;
    }

    /// <summary>
    /// Kept from the previous version: transposes the rotation part for lights and keeps the
    /// translation. If lights look wrong at depth > 1, this is the place to look - it depends on
    /// what your light emitter expects.
    /// </summary>
    static Matrix4x4 LightGlobalFromNode(Matrix4x4 nodeGlobal)
    {
        Matrix4x4 m = Matrix4x4.Transpose(nodeGlobal);
        m.M14 = m.M24 = m.M34 = 0f;
        m.M41 = nodeGlobal.M41;
        m.M42 = nodeGlobal.M42;
        m.M43 = nodeGlobal.M43;
        m.M44 = 1f;
        return m;
    }

    static Dictionary<string, ImportedNode> BuildNodeLookup(ImportedScene scene)
    {
        var lookup = new Dictionary<string, ImportedNode>(StringComparer.Ordinal);
        foreach (var n in scene.CollectNodes())
            lookup.TryAdd(n.Name, n);
        return lookup;
    }

    /// <summary>
    /// Matrix round-trips leave ~1e-7 noise (e.g. cos(90deg)). Snap anything within 1e-5 of an
    /// integer so "identity rotation" really is identity.
    /// </summary>
    static Matrix4x4 Snap(Matrix4x4 m) => new(
        SnapValue(m.M11), SnapValue(m.M12), SnapValue(m.M13), SnapValue(m.M14),
        SnapValue(m.M21), SnapValue(m.M22), SnapValue(m.M23), SnapValue(m.M24),
        SnapValue(m.M31), SnapValue(m.M32), SnapValue(m.M33), SnapValue(m.M34),
        SnapValue(m.M41), SnapValue(m.M42), SnapValue(m.M43), SnapValue(m.M44));

    static float SnapValue(float v)
    {
        float r = MathF.Round(v);
        return MathF.Abs(v - r) < 1e-5f ? r : v;
    }

    static void FlipWindingInPlace(int[] indices)
    {
        for (int i = 0; i + 2 < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
    }

    // =====================================================================
    //  Item config
    // =====================================================================

    /// <summary>
    /// Fix all provided axis/position offsets that are pure vectors as they are assumed to be in
    /// the original (Blender) coordinate space and need to be transformed into the target space.
    /// </summary>
    public void FixConfigAxes(ItemConfig itemConfig)
    {
        static Vector3 Fix(Vector3 v) => Vector3.Transform(v, BlenderToTarget);
       
        static Quaternion FixQuaternion(Quaternion q)
        {
            Quaternion matrixRotation = Quaternion.CreateFromRotationMatrix(BlenderToTarget);
            Quaternion matrixRotationInv = Quaternion.Conjugate(matrixRotation);
            return Quaternion.Normalize(
                Quaternion.Multiply(
                Quaternion.Multiply(matrixRotationInv, q),
                matrixRotation));
        }

        if (itemConfig.Waypoint != null)
        {
            if (itemConfig.Waypoint.DefaultGravitySpawn.HasValue)
                itemConfig.Waypoint.DefaultGravitySpawn = Fix(itemConfig.Waypoint.DefaultGravitySpawn.Value);
        }
        if (itemConfig.Placement != null)
        {
            if (itemConfig.Placement.CubeCenter.HasValue)
                itemConfig.Placement.CubeCenter = Fix(itemConfig.Placement.CubeCenter.Value);
            if (itemConfig.Placement.PivotPositions != null)
            {
                for (int i = 0; i < itemConfig.Placement.PivotPositions.Count; ++i)
                    itemConfig.Placement.PivotPositions[i] = Fix(itemConfig.Placement.PivotPositions[i]);
            }
            if (itemConfig.Placement.PivotRotations != null)
            {
                // Same formula as before, only the matrix now comes from the shared config.
                // (q' = C^-1 * q * C; the other order gave the right axis but the wrong sign
                //  for your pipeline.)
                for (int i = 0; i < itemConfig.Placement.PivotRotations.Count; ++i)
                    itemConfig.Placement.PivotRotations[i] = FixQuaternion(itemConfig.Placement.PivotRotations[i]);
            }
            if (itemConfig.Placement.MagnetPositions != null)
            {
                for (int i = 0; i < itemConfig.Placement.MagnetPositions.Count; ++i)
                    itemConfig.Placement.MagnetPositions[i] = Fix(itemConfig.Placement.MagnetPositions[i]);
            }
            if (itemConfig.Placement.MagnetRotations != null)
            {
                // NOTE: these Euler angles are hardcoded for the current Y->-Z / Z->Y mapping and
                // do NOT follow BlenderToTarget. If you change the axes above, this line must be
                // revisited.
                for (int i = 0; i < itemConfig.Placement.MagnetRotations.Count; ++i)
                    itemConfig.Placement.MagnetRotations[i] = new Vec3(
                        itemConfig.Placement.MagnetRotations[i].X - 90,
                        itemConfig.Placement.MagnetRotations[i].Z,
                        -itemConfig.Placement.MagnetRotations[i].Y);
            }

            if (itemConfig.Placement.PlacementClass != null)
            {
                if (itemConfig.Placement.PlacementClass.WorldDir.HasValue)
                    itemConfig.Placement.PlacementClass.WorldDir = Fix(itemConfig.Placement.PlacementClass.WorldDir.Value);
            }
        }
        foreach (var meshConfig in itemConfig.Meshes)
        {
            if (meshConfig.GameplayMainDir.HasValue)
                meshConfig.GameplayMainDir = Fix(meshConfig.GameplayMainDir.Value);
        }
        foreach (var movingGroup in itemConfig.MovingGroups)
        {
            var km = movingGroup.KinematicMovement;

            var (newTransAxis, transSign) = MapAxis(km.TransAxis);
            km.TransAxis = newTransAxis;
            if (transSign < 0f)
            {
                km.TransMin = -km.TransMin;
                km.TransMax = -km.TransMax;
            }

            // Only the axis is remapped here (as before). If the source axis maps with a negative
            // sign, any rotation angle/limits on it flip sign as well.
            var (newRotAxis, _) = MapAxis(km.RotAxis);
            km.RotAxis = newRotAxis;
        }
        foreach(var attachment in itemConfig.Attachments)
        {
            for (int i = 0; i < attachment.Positions.Length; ++i)
                attachment.Positions[i] = Fix(attachment.Positions[i]);
            for (int i = 0; i < attachment.Rotations.Length; ++i)
                attachment.Rotations[i] = FixQuaternion(attachment.Rotations[i]);
        }
    }

    /// <summary>Maps a source (Blender) axis onto the target axis it lands on, plus the sign.</summary>
    static (NPlugDyna_SKinematicConstraint.EAxis axis, float sign) MapAxis(NPlugDyna_SKinematicConstraint.EAxis source)
    {
        Vector3 image = source switch
        {
            NPlugDyna_SKinematicConstraint.EAxis.X => BlenderXInTarget,
            NPlugDyna_SKinematicConstraint.EAxis.Y => BlenderYInTarget,
            NPlugDyna_SKinematicConstraint.EAxis.Z => BlenderZInTarget,
            _ => throw new NotSupportedException($"Unsupported axis: {source}")
        };

        if (MathF.Abs(image.X) > 0.5f) return (NPlugDyna_SKinematicConstraint.EAxis.X, MathF.Sign(image.X));
        if (MathF.Abs(image.Y) > 0.5f) return (NPlugDyna_SKinematicConstraint.EAxis.Y, MathF.Sign(image.Y));
        return (NPlugDyna_SKinematicConstraint.EAxis.Z, MathF.Sign(image.Z));
    }
}
