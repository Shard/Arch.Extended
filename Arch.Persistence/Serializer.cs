using Arch.Core;
using Arch.Persistence.Aot;
using Nerdbank.MessagePack;
using System.Buffers;

namespace Arch.Persistence;

/// <summary>
/// The IArchSerializer interface for (de)serializing worlds and entities.
/// </summary>
public interface IArchSerializer
{
    /// <summary>
    /// Serializes a World to a byte array.
    /// </summary>
    byte[] Serialize(World world);

    /// <summary>
    /// Serializes a World to a Stream.
    /// </summary>
    void Serialize(Stream stream, World world);

    /// <summary>
    /// Deserializes a byte array into a World.
    /// </summary>
    World Deserialize(byte[] world);

    /// <summary>
    /// Deserializes a Stream into a World.
    /// </summary>
    World Deserialize(Stream stream);
}

/// <summary>
/// AOT-compatible binary serializer for Arch ECS worlds using Nerdbank.MessagePack.
/// </summary>
public class ArchBinarySerializer : IArchSerializer
{
    private readonly ArchSerializationContext _archContext;

    public ArchBinarySerializer() : this(Array.Empty<MessagePackConverter>())
    {
    }

    public ArchBinarySerializer(IEnumerable<MessagePackConverter> additionalConverters)
        : this(additionalConverters, null)
    {
    }

    /// <summary>
    /// Creates a serializer, optionally using a specific <paramref name="registry"/>
    /// (for test isolation with throwaway registries). When null, uses
    /// <see cref="ComponentTypeRegistry.Default"/>.
    /// </summary>
    public ArchBinarySerializer(IEnumerable<MessagePackConverter> additionalConverters, ComponentTypeRegistry? registry)
    {
        _archContext = new ArchSerializationContext(additionalConverters, registry);
    }

    /// <inheritdoc/>
    public byte[] Serialize(World world)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);

        WorldSerializer.SerializeWorld(ref writer, world, _archContext);

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <inheritdoc/>
    public void Serialize(Stream stream, World world)
    {
        var bytes = Serialize(world);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <inheritdoc/>
    public World Deserialize(byte[] world)
    {
        var reader = new MessagePackReader(world);
        return WorldSerializer.DeserializeWorld(ref reader, _archContext);
    }

    /// <inheritdoc/>
    public World Deserialize(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return Deserialize(memoryStream.ToArray());
    }
}
