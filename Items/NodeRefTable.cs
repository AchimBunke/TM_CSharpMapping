using GBX.NET.Engines.MwFoundations;
using System.Diagnostics.CodeAnalysis;

namespace TM_GenericMapping.Items;

internal class NodeRefTable
{
    public NodeRefTable() { }

    Dictionary<Type, Dictionary<int, CMwNod>> _indexToNode { get; } = new();
    Dictionary<Type, Dictionary<CMwNod, int>> _nodeToIndex { get; } = new();
    int _nextKey = 0;

    public void Clear()
    {
        _indexToNode.Clear();
        _nodeToIndex.Clear();
        _nextKey = 0;
    }

    public bool Register<T>(int key, T node) where T : CMwNod
    {
        if (node == null)
            return false;

        var type = typeof(T);

        if(!_indexToNode.TryGetValue(type, out var indexToNode))
        {
            indexToNode = new Dictionary<int, CMwNod>();
            _indexToNode[type] = indexToNode;
        }

        if(!_nodeToIndex.TryGetValue(type, out var nodeToIndex))
        {
            nodeToIndex = new Dictionary<CMwNod, int>();
            _nodeToIndex[type] = nodeToIndex;
        }

        if(!indexToNode.TryAdd(key, node))
            return false;
        if (!nodeToIndex.TryAdd(node, key))
            return false;

        _nextKey = Math.Max(_nextKey, key + 1);

        return true;
    }
    public bool Register<T>(T node, out int key) where T : CMwNod
    {
        key = GetNextKey();
        return Register(key, node);
    }

    private int GetNextKey()
    {
        return _nextKey++;
    }

    public bool TryGetNode<T>(int key, [NotNullWhen(true)] out T node) where T : CMwNod
    {
        var type = typeof(T);
        if (_indexToNode.TryGetValue(type, out var indexToNode))
        {
            if (indexToNode.TryGetValue(key, out var foundNode))
            {
                node = (T)foundNode;
                return true;
            }
        }
        node = default!;
        return false;
    }

    public bool TryGetKey<T>(T node, out int key) where T : CMwNod
    {
        var type = typeof(T);
        if (_nodeToIndex.TryGetValue(type, out var nodeToIndex))
        {
            if (nodeToIndex.TryGetValue(node, out var foundKey))
            {
                key = foundKey;
                return true;
            }
        }
        key = default;
        return false;
    }
}
