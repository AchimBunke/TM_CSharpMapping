using GBX.NET.Engines.MwFoundations;
using System.Diagnostics.CodeAnalysis;

namespace TM_GenericMapping.Items;

internal class NodeRefTable
{
    private const string AutoKeyPrefix = "__auto:";

    public NodeRefTable() { }

    Dictionary<Type, Dictionary<string, CMwNod>> _keyToNode { get; } = new();
    Dictionary<Type, Dictionary<CMwNod, string>> _nodeToKey { get; } = new();
    private readonly HashSet<string> _reservedKeys = new();
    int _nextAutoKey = 0;

    public void Clear()
    {
        _keyToNode.Clear();
        _nodeToKey.Clear();
        _nextAutoKey = 0;
        _reservedKeys.Clear();
    }

    public string ReserveKey<T>() where T : CMwNod
    {
        string key;
        do { key = $"{AutoKeyPrefix}{typeof(T).Name}:{_nextAutoKey++}"; }
        while (!_reservedKeys.Add(key)); // practically never loops, guards against pathological reuse
        return key;
    }

    public bool Register<T>(string key, T node) where T : CMwNod
    {
        if (node is null) return false;

        bool isReserved = _reservedKeys.Remove(key); // fine if it was reserved; also fine if it wasn't

        if (!isReserved && key.StartsWith(AutoKeyPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"Key '{key}' uses the reserved auto-key prefix but was never reserved via ReserveKey<T>().");

        return RegisterInternal(key, node);
    }
    public bool Register<T>(T node, out string key) where T : CMwNod
    {
        key = ReserveKey<T>();
        return Register(key, node);
    }
    private bool RegisterInternal<T>(string key, T node) where T : CMwNod
    {
        var type = typeof(T);
        if (!_keyToNode.TryGetValue(type, out var keyToNode)) _keyToNode[type] = keyToNode = new();
        if (!_nodeToKey.TryGetValue(type, out var nodeToKey)) _nodeToKey[type] = nodeToKey = new();

        if (!keyToNode.TryAdd(key, node)) return false;
        if (!nodeToKey.TryAdd(node, key)) return false;
        return true;
    }

    public bool TryGetNode<T>(string key, [NotNullWhen(true)] out T? node) where T : CMwNod
    {
        var type = typeof(T);
        if (_keyToNode.TryGetValue(type, out var keyToNode) && keyToNode.TryGetValue(key, out var found))
        {
            node = (T)found;
            return true;
        }
        node = default;
        return false;
    }

    public bool TryGetKey<T>(T node, [NotNullWhen(true)] out string key) where T : CMwNod
    {
        var type = typeof(T);
        if (_nodeToKey.TryGetValue(type, out var nodeToKey) && nodeToKey.TryGetValue(node, out var found))
        {
            key = found;
            return true;
        }
        key = null!;
        return false;
    }
}
