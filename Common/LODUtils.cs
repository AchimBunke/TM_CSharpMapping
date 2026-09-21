using System.Numerics;

namespace TM_GenericMapping.Common;

public static class LODUtils
{
    public static bool IsVisibleInLod(int lodMask, int lod)
    {
        return (lodMask & (1 << lod)) != 0;
    }
    public static int LodMaskFromLods(params int[] lods)
    {
        // Empty lods implies no LOD switching -> a single always-visible slot (lod mask 1).
        if (lods.Length == 0)
            return 1;

        int mask = 0;

        foreach (int lod in lods)
            mask |= 1 << lod;

        return mask;
    }

    public static bool IsVisibleInAllLods(int lodMask, int lodCount)
    {
        return lodMask == GetAllLodsMask(lodCount);
    }

    public static int GetAllLodsMask(int lodCount)
    {
        return (1 << lodCount) - 1;
    }
    public static int SetLod(int lodMask, int lod, bool enabled)
    {
        int bit = 1 << lod;

        return enabled
            ? lodMask | bit      // set bit to 1
            : lodMask & ~bit;    // set bit to 0
    }


    public static List<Vector2> ToLodRanges(int lodMask, float[] lodDistances)
    {
        List<Vector2> lodRanges = new List<Vector2>();
        for (int i = 0; i < lodDistances.Length; i++)
        {
            if (IsVisibleInLod(lodMask, i))
            {
                float minDistance = i == 0 ? 0 : lodDistances[i - 1];
                float maxDistance = lodDistances[i];
                lodRanges.Add(new Vector2(minDistance, maxDistance));
            }
        }
        lodRanges.Add(new Vector2(lodDistances.LastOrDefault(), float.PositiveInfinity)); // Add the last range to infinity
        return lodRanges;
    }
    public static List<int> ToLodIndexes(int lodMask, float[] lodDistances)
    {
        List<int> lodIndexes = new List<int>();

        int lodCount = lodDistances.Length + 1;

        for (int i = 0; i < lodCount; i++)
        {
            if ((lodMask & (1 << i)) != 0)
                lodIndexes.Add(i);
        }

        return lodIndexes;
    }
}

