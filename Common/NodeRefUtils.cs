using GBX.NET.Components;
using static GBX.NET.Engines.Meta.CPlugVegetTreeModel;

namespace TM_GenericMapping.Common;

public static class NodeRefUtils
{
    public static GbxRefTableFile CreateFile(string filePath)
    {
        var reftable = new GbxRefTable() { FileSystemPath = filePath };
        return reftable.CreateFile(filePath, 1, false);
    }
}
