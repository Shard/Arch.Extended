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
/// Registry mapping component types to their typed serializers using stable string names.
/// Components must be registered at startup before any serialization occurs.
/// Uses Type.FullName as the stable identifier so saves survive component reordering.
///
/// Supports both a process-global default (via static shims and <see cref="Default"/>)
/// for normal use and per-serializer instances for test isolation (e.g. simulating
/// unknown/future component types without polluting other tests).
/// </summary>
public sealed class ComponentTypeRegistry
{
    private readonly Dictionary<Type, IComponentSerializer> _serializers = new();
    private readonly Dictionary<string, Type> _nameToType = new();
    private readonly Dictionary<Type, string> _typeToName = new();

    /// <summary>
    /// The process-global default registry used by production code and static shims.
    /// </summary>
    public static ComponentTypeRegistry Default { get; } = new ComponentTypeRegistry();

    /// <summary>
    /// Register a component type for serialization.
    /// Uses compile-time generic instantiation for AOT compatibility.
    /// </summary>
    public void Register<T>() where T : struct, IShapeable<T>
    {
        var type = typeof(T);
        if (_serializers.ContainsKey(type))
            return;

        var name = type.FullName!;
        _nameToType[name] = type;
        _typeToName[type] = name;
        _serializers[type] = new NerdbankComponentSerializer<T>();
    }

    /// <summary>
    /// Register a transient component type's name for deserialization resolution.
    /// No serializer is created — component data is skipped during load but the
    /// component slot is preserved on entities so game code can Set/Get it.
    /// </summary>
    public void RegisterTransient(Type type)
    {
        var name = type.FullName!;
        if (_nameToType.ContainsKey(name))
            return;

        _nameToType[name] = type;
        _typeToName[type] = name;
    }

    /// <summary>
    /// Get the serializer for a component type.
    /// </summary>
    public IComponentSerializer? GetSerializer(Type type)
    {
        return _serializers.TryGetValue(type, out var serializer) ? serializer : null;
    }

    /// <summary>
    /// Get the stable type name for serialization.
    /// </summary>
    public string? GetTypeName(Type type)
    {
        return _typeToName.TryGetValue(type, out var name) ? name : null;
    }

    /// <summary>
    /// Resolve a type from its serialized name.
    /// </summary>
    public Type? GetTypeFromName(string name)
    {
        return _nameToType.TryGetValue(name, out var type) ? type : null;
    }

    /// <summary>
    /// Check if a type is registered.
    /// </summary>
    public bool IsRegistered(Type type) => _serializers.ContainsKey(type);

    /// <summary>
    /// Clear all registrations. Primarily for testing (use local instances for
    /// throwaway registries instead of mutating the global).
    /// </summary>
    public void Clear()
    {
        _serializers.Clear();
        _nameToType.Clear();
        _typeToName.Clear();
    }

}
