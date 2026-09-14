using GBX.NET;
using GBX.NET.Engines.Meta;
using GBX.NET.Engines.MetaNotPersistent;
using GBX.NET.Engines.Plug;
using System.Numerics;
using System.Reflection;
using TM_GenericMapping.Common;
using TmEssentials;
using static GBX.NET.Engines.Plug.CPlugPrefab;
using static GBX.NET.Engines.Plug.CPlugSkel;
using static GBX.NET.Engines.Plug.CPlugSolid2Model;

namespace TM_GenericMapping.Items.MeshCompilation;

public static class GbxItemUtils
{
    public static CPlugSpawnModel CreateSpawnModel()
    {
        var spawnModel = new CPlugSpawnModel()
        {
            DefaultGravitySpawn = new Vec3(0, -1, 0),
            TorqueX = 0,
            TorqueDuration = TimeInt32.Zero,
            Loc = IsoFromTransform(Vec3.Zero, Quaternion.Identity),
        };
        var c = spawnModel.CreateChunk<CPlugSpawnModel.Chunk0917A000>();
        c.Version = 3;
        return spawnModel;
    }
    public static Iso4 IsoFromTransform(Vec3 location, Quaternion rotation)
    {
        var m = Matrix4x4.CreateFromQuaternion(rotation);

        return new Iso4(
            m.M11, m.M12, m.M13,
            m.M21, m.M22, m.M23,
            m.M31, m.M32, m.M33,
            location.X, location.Y, location.Z
        );
    }
    public static Iso4 IsoFromPitchYawRoll(Vec3 location, float pitchDeg, float yawDeg, float rollDeg)
    {
        float p = pitchDeg * MathUtils.Deg2Rad;
        float y = yawDeg * MathUtils.Deg2Rad;
        float r = rollDeg * MathUtils.Deg2Rad;

        float cp = MathF.Cos(p), sp = MathF.Sin(p);
        float cy = MathF.Cos(y), sy = MathF.Sin(y);
        float cr = MathF.Cos(r), sr = MathF.Sin(r);

        // M = Rz(roll) * Ry(yaw) * Rx(pitch)
        float XX = cr * cy;
        float XY = sp * sy * cr + sr * cp;
        float XZ = sp * sr - sy * cp * cr;

        float YX = -sr * cy;
        float YY = -sp * sr * sy + cp * cr;
        float YZ = sp * cr + sr * sy * cp;

        float ZX = sy;
        float ZY = -sp * cy;
        float ZZ = cp * cy;

        return new Iso4(
            XX, XY, XZ,
            YX, YY, YZ,
            ZX, ZY, ZZ,
            location.X, location.Y, location.Z
        );
    }


    public static Vec3[] ComputeSmoothNormals(Vec3[] positions, int[] indices)
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


    public static string MaterialToName(CPlugMaterialUserInst mat)
    {
        if (!string.IsNullOrWhiteSpace(mat.MaterialName))
            return mat.MaterialName;
        if (!string.IsNullOrWhiteSpace(mat.Link))
            return string.Join("\\", mat.Link.Split('\\').TakeLast(2));
        return "Unknown Material";
    }
    public static CPlugMaterialUserInst CreateErrorMat()
    {
        var mat = new CPlugMaterialUserInst()
        {
            MaterialName = "",
            Model = "",
            BaseTexture = "",

        };
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD000>(out var c1);
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD001>(out var c2);
        c2.U02 = 0;
        mat.TryCreateChunk<CPlugMaterialUserInst.Chunk090FD002>(out var c3);
        return mat;
    }

    public static PreLightGen CreateEmtpyPreLightGen()
    {
        return new PreLightGen()
        {
            Version = 1,
            U01 = 1,
            U03 = true,
            U12 = [],
            UvGroups = [],
        };
    }
    public static PreLightGen? ComputePreLightGenFromMeshData(NormalizedMeshV3 mesh)
    {
        if (mesh.LightmapCoords == null)
            return null;
        var preLightGen = CreateEmtpyPreLightGen();
        preLightGen.U08 = float.MaxValue;
        preLightGen.U09 = float.MaxValue;
        preLightGen.U10 = float.MinValue;
        preLightGen.U11 = float.MinValue;
        preLightGen.U02 = ComputeLightMapSizeLengthMeters(mesh.Positions, mesh.LightmapCoords, mesh.Indices);
        preLightGen.U04 = mesh.LightmapCoords.Min(uv => uv.X);
        preLightGen.U05 = mesh.LightmapCoords.Min(uv => uv.Y);
        preLightGen.U06 = mesh.LightmapCoords.Max(uv => uv.X);
        preLightGen.U07 = mesh.LightmapCoords.Max(uv => uv.Y);
        return preLightGen;
    }
    public static float ComputeLightMapSizeLengthMeters(
        IReadOnlyList<Vec3> positions,
        IReadOnlyList<Vec2> lightmapUVs,
        IReadOnlyList<int> triangleIndices)
    {
        double sumWorldLen = 0.0;
        double sumUvLen = 0.0;

        for (int i = 0; i < triangleIndices.Count; i += 3)
        {
            int i0 = triangleIndices[i], i1 = triangleIndices[i + 1], i2 = triangleIndices[i + 2];
            int[] tri = { i0, i1, i2 };

            for (int e = 0; e < 3; e++)
            {
                int a = tri[e];
                int b = tri[(e + 1) % 3];

                sumWorldLen += Vector3.Distance(positions[a], positions[b]);
                sumUvLen += Vector2.Distance(lightmapUVs[a], lightmapUVs[b]);
            }
        }

        return sumUvLen < 1e-9 ? 0f : (float)(sumWorldLen / sumUvLen);
    }

    public static PreLightGen? MergePreLightGenerator(PreLightGen? preLightGen1, PreLightGen? preLightGen2)
    {
        if (preLightGen1 is null) return preLightGen2;
        if (preLightGen2 is null) return preLightGen1;

        var preLightGen = CreateEmtpyPreLightGen();

        preLightGen.U02 = Math.Max(preLightGen1.U02, preLightGen2.U02); // LMSizeLengthMeters
        preLightGen.U04 = Math.Min(preLightGen1.U04, preLightGen2.U04); // Min UV-X
        preLightGen.U05 = Math.Min(preLightGen1.U05, preLightGen2.U05); // Min UV-Y
        preLightGen.U06 = Math.Max(preLightGen1.U06, preLightGen2.U06); // Max UV-X
        preLightGen.U07 = Math.Max(preLightGen1.U07, preLightGen2.U07); // Max UV-Y

        preLightGen.U08 = Math.Max(preLightGen1.U08, preLightGen2.U08); // Max (float.Max i think almost always)
        preLightGen.U09 = Math.Max(preLightGen1.U09, preLightGen2.U09); // Max (float.Max i think almost always)
        preLightGen.U10 = Math.Min(preLightGen1.U10, preLightGen2.U10); // Min (float.Max i think almost always)
        preLightGen.U11 = Math.Min(preLightGen1.U11, preLightGen2.U11); // Min (float.Max i think almost always)
        return preLightGen;

    }


    public static (CPlugSkel skel, Socket[] sockets) ParseSkel(CPlugSkel skel)
    {
        var socketField = typeof(CPlugSkel).GetField("sockets",
         BindingFlags.NonPublic | BindingFlags.Instance);
        var sockets = (Socket[])socketField!.GetValue(skel)!;

        return (skel, sockets);
    }

    public static EntRef CreateEntRef()
    {
        var entRef = new EntRef
        {
            Model = null,
            Position = Vec3.Zero,
            Rotation = Quat.Identity,
            Params = null,
            ModelFile = null,
            U01 = "",
        };
        return entRef;
    }
    public static CPlugPrefab CreateCPlugPrefab()
    {
        var prefab = new CPlugPrefab()
        {
            Ents = [],
            FileWriteTime = DateTime.Now,
            Version = 11,
        };
        return prefab;
    }
    public static NPlugItem_SVariantList CreateVariantList()
    {
        var variantList = new NPlugItem_SVariantList()
        {
            Version = 1,
        };
        return variantList;
    }
    public static NPlugItem_SVariant CreateVariant()
    {
        return new NPlugItem_SVariant()
        {
          
        };
    }

    public static BoxAligned BuildBoxAligned(NormalizedMeshV3 mesh)
    {
        Vec3 min = mesh.Positions[0];
        Vec3 max = mesh.Positions[0];

        foreach (var p in mesh.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var center = (min + max) * 0.5f;
        var extent = (max - min) * 0.5f;

        var box = new BoxAligned(
            center.X, center.Y, center.Z,
            extent.X, extent.Y, extent.Z
        );
        return box;
    }
    public static BoxAligned BuildBoxAligned(CPlugVisual visual)
    {
        if(visual.VertexStreams.Count == 0 || visual.VertexStreams[0]!.Positions!.Length == 0)
            return new BoxAligned(0, 0, 0, 0, 0, 0);

        Vec3 min = visual.VertexStreams![0].Positions![0];
        Vec3 max = visual.VertexStreams![0].Positions![0];
        foreach (var vertexStream in visual.VertexStreams)
        {
            foreach (var p in vertexStream.Positions ?? [])
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        var center = (min + max) * 0.5f;
        var extent = (max - min) * 0.5f;

        var box = new BoxAligned(
            center.X, center.Y, center.Z,
            extent.X, extent.Y, extent.Z
        );
        return box;
    }


    public static List<Socket> ParseSockets(CPlugSkel skel)
    {
        var socketField = typeof(CPlugSkel).GetField("sockets",
         BindingFlags.NonPublic | BindingFlags.Instance);
        var sockets = (Socket[])socketField!.GetValue(skel)!;
        return sockets.ToList();
    }
    public static CPlugSkel CreateSkel(List<Socket> sockets)
    {
        var skel = new CPlugSkel()
        {
            Name = "",
            U04 = [],
            U05 = 1,
            U06 = 0,
            U07 = [],
            U08 = [],
        };
        var c = skel.CreateChunk<CPlugSkel.Chunk090BA000>();
        c.Version = 20;

        var socketField = typeof(CPlugSkel).GetField("sockets",
            BindingFlags.NonPublic | BindingFlags.Instance);
        socketField?.SetValue(skel, sockets.ToArray());

        var jointExprsField = typeof(CPlugSkel).GetField("jointExprs",
           BindingFlags.NonPublic | BindingFlags.Instance);
        jointExprsField?.SetValue(skel, new JointExpr[0]);

        var jointsField = typeof(CPlugSkel).GetField("joints",
           BindingFlags.NonPublic | BindingFlags.Instance);
        jointsField?.SetValue(skel, new Joint[0]);

        return skel;
    }
    public static CPlugLightUserModel CreateLightUserModel(NormalizedLightV3 light, InstanceSettings instanceSetting)
    {
        var lightModel = ObjectCloner.DeepCloneObject(light.LightModel)!;
        var c = lightModel.GetChunk<CPlugLightUserModel.Chunk090F9000>()!;
        if(instanceSetting.LightTypeOverride.HasValue)
            c.U01 = (int)instanceSetting.LightTypeOverride.Value;
        return lightModel;
    }


    public static Vec3[]? GetTangentUs(CPlugVertexStream vertexStream)
    {
        var tangentUsField = typeof(CPlugVertexStream).GetField("tangentUs",
        BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (Vec3[]?)tangentUsField.GetValue(vertexStream);

    }
   
    public static void SetTangentUs(CPlugVertexStream vertexStream, Vec3[]? tangentUs)
    {
        var tangentUsField = typeof(CPlugVertexStream).GetField("tangentUs",
        BindingFlags.NonPublic | BindingFlags.Instance)!;

        tangentUsField.SetValue(vertexStream, tangentUs);
    }

    public static Vec3[]? GetTangentVs(CPlugVertexStream vertexStream)
    {
        var tangentVsField = typeof(CPlugVertexStream).GetField("tangentVs",
        BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (Vec3[]?)tangentVsField.GetValue(vertexStream);

    }
    public static void SetTangentVs(CPlugVertexStream vertexStream, Vec3[]? tangentVs)
    {
        var tangentVsField = typeof(CPlugVertexStream).GetField("tangentVs",
        BindingFlags.NonPublic | BindingFlags.Instance)!;

        tangentVsField.SetValue(vertexStream, tangentVs);
    }

}
