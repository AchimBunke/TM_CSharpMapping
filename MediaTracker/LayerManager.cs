namespace TM_GenericMapping.MediaTracker;

public static class LayerManager
{
    private static Dictionary<string, uint> layers = new();
    private static int nextBit = 0;

    public static uint GetLayerMask(string layerName)
    {
        if (!layers.TryGetValue(layerName, out uint bit))
        {
            if (nextBit >= 32)
                throw new InvalidOperationException("Max 32 layers supported.");

            bit = 1u << nextBit++;
            layers[layerName] = bit;
        }
        return bit;
    }
    public static bool IsInLayer(uint layerMask, uint layer) => (layerMask & layer) != 0;
}
