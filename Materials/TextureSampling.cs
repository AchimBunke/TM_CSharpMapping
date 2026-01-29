using System.Drawing;
using System.Numerics;
using TM_GenericMapping.Common;
using static GBX.NET.Engines.Plug.CPlugMaterialUserInst;

namespace TM_GenericMapping.Materials;

public static class TextureSampling
{
    public static bool TrySampleDiffuse(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV, out Color color)
    {
        color = Color.Black;
        if (material.Diffuse == null)
            return false;
        color = material.Diffuse.Sample(u, v, textureSizeInMeters, tilingU, tilingV);
        return true;
    }
    public static bool TrySampleIllum(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV, out Color color)
    {
        color = Color.Black;
        if (material.Illum == null)
            return false;
        color = material.Illum.Sample(u, v, textureSizeInMeters, tilingU, tilingV);
        return true;
    }
    public static bool TrySampleNormal(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV, out Color color)
    {
        color = Color.Black;
        if (material.Normal == null)
            return false;
        color = material.Normal.Sample(u, v, textureSizeInMeters, tilingU, tilingV);
        return true;
    }
    public static bool TrySampleRoughness(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV, out Color color)
    {
        color = Color.Black;
        if (material.Roughness == null)
            return false;
        color = material.Roughness.Sample(u, v, textureSizeInMeters, tilingU, tilingV);
        return true;
    }
    public static Color SampleColor(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV)
    {
        TrySampleDiffuse(material, u, v, textureSizeInMeters, tilingU, tilingV, out var diffuse);
        TrySampleIllum(material, u, v, textureSizeInMeters, tilingU, tilingV, out var illum);
        return (diffuse.ToVector4() + illum.ToVector4()).ToColor();
    }
    public static Color SampleColor(Material material, float u, float v, float textureSizeInMeters, ETexAddress tilingU, ETexAddress tilingV, Vector3 viewDir, float ambient = 0.3f, Vector3? light = null)
    {
        Vector3 defaultLight = light ?? new Vector3(0.5f, 1f, 0.5f);
        TrySampleDiffuse(material, u, v, textureSizeInMeters, tilingU, tilingV, out var diffuse);
        TrySampleIllum(material, u, v, textureSizeInMeters, tilingU, tilingV, out var illum);
        TrySampleNormal(material, u, v, textureSizeInMeters, tilingU, tilingV, out var normalColor);
        TrySampleRoughness(material, u, v, textureSizeInMeters, tilingU, tilingV, out var roughnessColor);
        var normalVec = NormalFromColor(normalColor);
        float rough = roughnessColor.R / 255f; // 0 = smooth, 1 = rough

        Vector3 halfVec = Vector3.Normalize(defaultLight + viewDir);
        float specularStrength = MathF.Pow(Math.Max(0, Vector3.Dot(normalVec, halfVec)), (1f - rough) * 128f);

        float diffuseLighting = Math.Max(ambient, Vector3.Dot(normalVec, defaultLight)); // 0.3 = ambient

        return (diffuse.ToVector4() * diffuseLighting + new Vector4(1, 1, 1, 0) * specularStrength * 0.5f + illum.ToVector4()).ToColor();
    }
    public static Vector3 NormalFromColor(Color normalColor)
    {
        // Convert RGB (0-255) to XYZ (-1 to 1)
        float x = (normalColor.R / 255f) * 2f - 1f;
        float y = (normalColor.G / 255f) * 2f - 1f;
        float z = (normalColor.B / 255f) * 2f - 1f;

        return Vector3.Normalize(new Vector3(x, y, z));
    }
    public static string MaterialLinkToMaterialName(string link)
        => Path.GetFileName(link);


}
