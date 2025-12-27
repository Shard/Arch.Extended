using Nerdbank.MessagePack;
using PolyType;

namespace Arch.Persistence.Aot;

/// <summary>
/// Interface for type-erased component serialization.
/// Allows serializing component arrays without knowing the concrete type at compile time.
/// </summary>
public interface IComponentSerializer
{
    void Serialize(ref MessagePackWriter writer, Array array, int count, MessagePackSerializer serializer);
    Array Deserialize(ref MessagePackReader reader, int count, MessagePackSerializer serializer);
}

/// <summary>
/// Registry mapping component types to their typed serializers.
/// Components must be registered at startup before any serialization occurs.
/// </summary>
public static class ComponentTypeRegistry
{
    private static readonly Dictionary<Type, IComponentSerializer> _serializers = new();
    private static readonly Dictionary<int, Type> _idToType = new();
    private static readonly Dictionary<Type, int> _typeToId = new();
    private static int _nextId = 0;

    /// <summary>
    /// Register a component type for serialization.
    /// Uses compile-time generic instantiation for AOT compatibility.
    /// </summary>
    public static void Register<T>() where T : struct, IShapeable<T>
    {
        var type = typeof(T);
        if (_serializers.ContainsKey(type))
            return;

        var id = _nextId++;
        _idToType[id] = type;
        _typeToId[type] = id;
        _serializers[type] = new NerdbankComponentSerializer<T>();
    }

    /// <summary>
    /// Get the serializer for a component type.
    /// </summary>
    public static IComponentSerializer? GetSerializer(Type type)
    {
        return _serializers.TryGetValue(type, out var serializer) ? serializer : null;
    }

    /// <summary>
    /// Get the type ID for serialization.
    /// </summary>
    public static int GetTypeId(Type type)
    {
        return _typeToId.TryGetValue(type, out var id) ? id : -1;
    }

    /// <summary>
    /// Get the type from its serialization ID.
    /// </summary>
    public static Type? GetTypeFromId(int id)
    {
        return _idToType.TryGetValue(id, out var type) ? type : null;
    }

    /// <summary>
    /// Check if a type is registered.
    /// </summary>
    public static bool IsRegistered(Type type) => _serializers.ContainsKey(type);

    /// <summary>
    /// Clear all registrations. Primarily for testing.
    /// </summary>
    public static void Clear()
    {
        _serializers.Clear();
        _idToType.Clear();
        _typeToId.Clear();
        _nextId = 0;
    }
}
