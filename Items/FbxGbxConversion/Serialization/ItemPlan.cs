namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

public record FbxGbxPlanNode(string Name, string? OriginalNode, int[] LodIndices);
public record FbxGbxPlanGroup(string Key, string Type, float? LightmapSize, float[] LodDistances, FbxGbxPlanNode[] Nodes);
public record FbxGbxPlanReport(int Version, FbxGbxPlanGroup[] Groups);
