using GBX.NET.Components;
using static GBX.NET.Engines.Meta.CPlugVegetTreeModel;

namespace TM_GenericMapping.Common;

public static class NodeRefUtils
{
    public static GbxRefTableFile CreateFile(string filePath)
    {
        var realPath = FileUtils.ResolveRealPath(filePath);
        var reftable = new GbxRefTable() { FileSystemPath = realPath };
        return reftable.CreateFile(realPath, 1, false);
    }
}
