
using Silk.NET.Assimp;
using System.Numerics;
using TM_GenericMapping.Common;
using TM_GenericMapping.Items.MeshCompilation;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace TM_GenericMapping.Items.FbxGbxConversion.Importing;

/// <summary>
/// Assimp-backed implementation of <see cref="ISceneImporter"/>. This is the single place that
/// knows about Assimp types, coordinate space, unit scale (currently cm -&gt; m), and axis conventions.
/// If the import library is ever swapped, only a new <see cref="ISceneImporter"/> implementation
/// needs to be written - the rest of the conversion pipeline works exclusively against the
/// neutral <see cref="ImportedScene"/> DTOs.
/// </summary>
public class SilkAssimpSceneImporter : ISceneImporter
{
    private readonly float _scale;
    public SilkAssimpSceneImporter(float scale)
    {
        _scale = scale;
    }
    static readonly Matrix4x4 GbxCoordinateSpaceFixup =
        Matrix4x4.Create(
            1,0,0,0,
            0,1,0,0,
            0,0,1,0,
            0,0,0,1) *
       
        Matrix4x4.CreateScale(new Vector3(0.01f, 0.01f, 0.01f));
    public  ImportedScene Import(Stream stream)
    {
        using var context = Assimp.GetApi();

        unsafe
        {
            var store = context.CreatePropertyStore();

            //byte[] key = Encoding.UTF8.GetBytes("AI_CONFIG_GLOBAL_SCALE_FACTOR_KEY\0");
            //context.SetImportPropertyFloat(
            //        store,
            //        "GLOBAL_SCALE_FACTOR",
            //        0.01f
            //    );
           


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

                    ApplyGbxCoordinateSpaceFixup(importedScene);

                    return importedScene;
                }
                finally
                {
                    context.ReleaseImport(scene);
                }

            }
        }

       

        return null;
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
        if(metaData is null)
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
            var nodesByName = importedScene
                .CollectNodes()
                .ToDictionary(
                    n => n.Name,
                    n => n,
                    StringComparer.Ordinal);

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

        Matrix4x4 local = Matrix4x4.Transpose(node->MTransformation);
        Matrix4x4 global = parentGlobal * local;
       
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



    static float GetDeterminant3x3(Matrix4x4 m)
    {
        // Assimp.Matrix4x4 fields are A1..D4 (row-major: A=row1, B=row2, C=row3)
        return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
             - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
             + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
    }

    static int[] FlipWinding(int[] indices)
    {
        var result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            result[i] = indices[i];
            result[i + 1] = indices[i + 2]; // swap 1 and 2
            result[i + 2] = indices[i + 1];
        }
        return result;
    }

    void ApplyGbxCoordinateSpaceFixup(ImportedScene scene)
    {
        Matrix4x4 SC = new Matrix4x4(
            100f, 0f, 0f, 0f,
              0f, 0f, -100f, 0f,
              0f, 100f, 0f, 0f,
              0f, 0f, 0f, 1f);

        Matrix4x4 C = new Matrix4x4(
          1f, 0f, 0f, 0f,
            0f, 0f, -1f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 0f, 1f);

        Matrix4x4.Invert(SC, out Matrix4x4 scInv);
        scInv *= Matrix4x4.CreateScale(_scale); // scale

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Length; i++)
                mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], C);

            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.Normals[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[i], C));

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Length; i++)
                    mesh.Tangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Tangents[i], C));

            if (mesh.BiTangents is not null)
                for (int i = 0; i < mesh.BiTangents.Length; i++)
                    mesh.BiTangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.BiTangents[i], C));
        }


        // 2) Strip C back out of every node's local transform, then recompute
        //    globals top-down using the SAME composition your ConvertNode uses.
        void FixupNode(ImportedNode node, Matrix4x4 parentGlobal)
        {
            Matrix4x4 l = node.LocalTransform;

            Matrix4x4 linearOnly = l;
            linearOnly.M41 = linearOnly.M42 = linearOnly.M43 = 0f;

            Matrix4x4 rContainer = scInv * linearOnly;   // conjugates the baked conversion out
            rContainer.M41 = l.M41 / 100f * _scale;                      // translation cm -> m
            rContainer.M42 = l.M42 / 100f * _scale;
            rContainer.M43 = l.M43 / 100f * _scale;
            rContainer.M44 = 1f;

            node.LocalTransform = rContainer;
            node.GlobalTransform = parentGlobal * node.LocalTransform;
           
            foreach (var child in node.Children)
                FixupNode(child, node.GlobalTransform);
        }

        foreach(var child in scene.RootNode.Children)
        {
            FixupNode(child, Matrix4x4.Identity);
        }



        foreach (var light in scene.Lights)
        {
            var node = scene.CollectNodes().FirstOrDefault(n => n.Name == light.NodeName);


            Matrix4x4 baseTransform = node.GlobalTransform;

            var lightRotFix = baseTransform;
            lightRotFix = Matrix4x4.Transpose(lightRotFix);
            light.GlobalTransform = lightRotFix with
            {
                M41 = baseTransform.M41,
                M42 = baseTransform.M42,
                M43 = baseTransform.M43
            };
        }
    }

    //static void ApplyGbxCoordinateSpaceFixup(ImportedScene scene)
    //{
    //    Matrix4x4.Invert(GbxCoordinateSpaceFixup, out var inverseFixup);
    //    Matrix4x4 normalFixup = Matrix4x4.Transpose(inverseFixup);

    //    foreach (var node in scene.CollectNodes())
    //    {
    //        node.LocalTransform = inverseFixup * node.LocalTransform * GbxCoordinateSpaceFixup;
    //        node.GlobalTransform = inverseFixup * node.GlobalTransform * GbxCoordinateSpaceFixup;

    //    }

    //    foreach (var mesh in scene.Meshes)
    //    {
    //        //for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
    //        //    (mesh.Indices[i + 1], mesh.Indices[i + 2]) = (mesh.Indices[i + 2], mesh.Indices[i + 1]);

    //        for (int i = 0; i < mesh.Positions.Length; i++)
    //            mesh.Positions[i] = Vector3.Transform(mesh.Positions[i], GbxCoordinateSpaceFixup);

    //        for (int i = 0; i < mesh.Normals.Length; i++)
    //            mesh.Normals[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Normals[i], normalFixup));

    //        if (mesh.Tangents is not null)
    //            for (int i = 0; i < mesh.Tangents.Length; i++)
    //                mesh.Tangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.Tangents[i], GbxCoordinateSpaceFixup));

    //        if (mesh.BiTangents is not null)
    //            for (int i = 0; i < mesh.BiTangents.Length; i++)
    //                mesh.BiTangents[i] = Vector3.Normalize(Vector3.TransformNormal(mesh.BiTangents[i], GbxCoordinateSpaceFixup));
    //    }

    //    foreach (var light in scene.Lights)
    //    {
    //        light.Direction = Vector3.Normalize(Vector3.TransformNormal(light.Direction, GbxCoordinateSpaceFixup));
    //        light.GlobalTransform *= GbxCoordinateSpaceFixup;
    //    }
    //}


}



