namespace TM_GenericMapping.Items.FbxGbxConversion.Serialization;

public record FbxGbxPlanNode(string Name, int[] LodIndices);
public record FbxGbxPlanGroup(string Key, string Type, string? OriginalGroupId, float[] LodDistances, FbxGbxPlanNode[] Nodes);
public record FbxGbxPlanReport(int Version, FbxGbxPlanGroup[] Groups);
