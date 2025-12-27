using Arch.Core;
using Arch.Core.Extensions;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using Arch.LowLevel.Jagged;
using Arch.Persistence.Aot;
using Nerdbank.MessagePack;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Arch.Persistence;

/// <summary>
/// Converter for Entity structs (just Id and Version).
/// </summary>
public class EntityConverter : MessagePackConverter<Entity>
{
    /// <summary>
    /// The world ID to use when deserializing entities.
    /// Must be set before deserialization.
    /// </summary>
    public int WorldId { get; set; }

    public override Entity Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var count = reader.ReadArrayHeader();
        var id = reader.ReadInt32();
        var version = reader.ReadInt32();
        return DangerousEntityExtensions.CreateEntityStruct(id, WorldId, version);
    }

    public override void Write(ref MessagePackWriter writer, in Entity value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(2);
        writer.Write(value.Id);
        writer.Write(value.Version);
    }
}

/// <summary>
/// Converter for ComponentType.
/// </summary>
public class ComponentTypeConverter : MessagePackConverter<ComponentType>
{
    public override ComponentType Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var count = reader.ReadArrayHeader();
        var id = reader.ReadInt32();
        var bytesize = reader.ReadInt32();
        return new ComponentType(id, bytesize);
    }

    public override void Write(ref MessagePackWriter writer, in ComponentType value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(2);
        writer.Write(value.Id);
        writer.Write(value.ByteSize);
    }
}

/// <summary>
/// Converter for Signature (component type collection).
/// </summary>
public class SignatureConverter : MessagePackConverter<Signature>
{
    public override Signature Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var count = reader.ReadArrayHeader();
        var componentTypes = new ComponentType[count];

        for (var i = 0; i < count; i++)
        {
            var typeCount = reader.ReadArrayHeader();
            var id = reader.ReadInt32();
            var bytesize = reader.ReadInt32();
            componentTypes[i] = new ComponentType(id, bytesize);
        }

        return new Signature(componentTypes);
    }

    public override void Write(ref MessagePackWriter writer, in Signature value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(value.Count);
        foreach (var type in value.Components)
        {
            writer.WriteArrayHeader(2);
            writer.Write(type.Id);
            writer.Write(type.ByteSize);
        }
    }
}

/// <summary>
/// Converter for EntityData (entity slot information).
/// </summary>
public class EntityDataConverter : MessagePackConverter<EntityData>
{
    public override EntityData Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var count = reader.ReadArrayHeader();
        var chunkIndex = reader.ReadInt32();
        var entityIndex = reader.ReadInt32();
        var version = reader.ReadInt32();
        return new EntityData(null!, new Slot(entityIndex, chunkIndex), version);
    }

    public override void Write(ref MessagePackWriter writer, in EntityData value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(3);
        writer.Write(value.Slot.ChunkIndex);
        writer.Write(value.Slot.Index);
        writer.Write(value.Version);
    }
}

/// <summary>
/// Converter for JaggedArray of EntityData.
/// </summary>
public class JaggedArrayEntityDataConverter : MessagePackConverter<JaggedArray<EntityData>>
{
    private const int CpuL1CacheSize = 16_384;
    private static readonly EntityData Filler = new(null!, new Slot(-1, -1), -1);

    public override JaggedArray<EntityData> Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var outerCount = reader.ReadArrayHeader();
        var capacity = reader.ReadInt32();
        var jaggedArray = new JaggedArray<EntityData>(CpuL1CacheSize / Unsafe.SizeOf<EntityData>(), Filler, capacity);

        for (var i = 0; i < capacity; i++)
        {
            var itemCount = reader.ReadArrayHeader();
            var chunkIndex = reader.ReadInt32();
            var entityIndex = reader.ReadInt32();
            var version = reader.ReadInt32();
            var item = new EntityData(null!, new Slot(entityIndex, chunkIndex), version);
            jaggedArray.Add(i, item);
        }

        return jaggedArray;
    }

    public override void Write(ref MessagePackWriter writer, in JaggedArray<EntityData> value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(1 + value.Capacity);
        writer.Write(value.Capacity);
        for (var i = 0; i < value.Capacity; i++)
        {
            var item = value[i];
            writer.WriteArrayHeader(3);
            writer.Write(item.Slot.ChunkIndex);
            writer.Write(item.Slot.Index);
            writer.Write(item.Version);
        }
    }
}

/// <summary>
/// Converter for recycled entity ID list.
/// </summary>
public class RecycledIdsConverter : MessagePackConverter<List<(int, int)>>
{
    public override List<(int, int)> Read(ref MessagePackReader reader, SerializationContext context)
    {
        context.DepthStep();
        var count = reader.ReadArrayHeader();
        var result = new List<(int, int)>(count);

        for (var i = 0; i < count; i++)
        {
            var itemCount = reader.ReadArrayHeader();
            var first = reader.ReadInt32();
            var second = reader.ReadInt32();
            result.Add((first, second));
        }

        return result;
    }

    public override void Write(ref MessagePackWriter writer, in List<(int, int)> value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(value.Count);
        foreach (var (first, second) in value)
        {
            writer.WriteArrayHeader(2);
            writer.Write(first);
            writer.Write(second);
        }
    }
}

/// <summary>
/// Serialization context for Arch ECS world serialization.
/// Holds converters and state needed during serialization.
/// </summary>
public class ArchSerializationContext
{
    public EntityConverter EntityConverter { get; } = new();
    public ComponentTypeConverter ComponentTypeConverter { get; } = new();
    public SignatureConverter SignatureConverter { get; } = new();
    public EntityDataConverter EntityDataConverter { get; } = new();
    public JaggedArrayEntityDataConverter JaggedArrayConverter { get; } = new();
    public RecycledIdsConverter RecycledIdsConverter { get; } = new();
    public MessagePackSerializer Serializer { get; private set; }

    // State during deserialization
    public World? World { get; set; }
    public Archetype? CurrentArchetype { get; set; }
    public Signature CurrentSignature { get; set; }
    public int[]? CurrentLookupArray { get; set; }

    public ArchSerializationContext() : this(Array.Empty<MessagePackConverter>())
    {
    }

    public ArchSerializationContext(IEnumerable<MessagePackConverter> additionalConverters)
    {
        // Create serializer with custom converters using the 'with' pattern
        var serializer = new MessagePackSerializer();

        // Add all converters
        var allConverters = new List<MessagePackConverter>
        {
            EntityConverter,
            ComponentTypeConverter,
            SignatureConverter,
            EntityDataConverter,
            JaggedArrayConverter,
            RecycledIdsConverter
        };
        allConverters.AddRange(additionalConverters);

        Serializer = serializer with
        {
            Converters = [.. serializer.Converters, .. allConverters]
        };
    }
}

/// <summary>
/// Static helper methods for world serialization using Nerdbank.MessagePack.
/// </summary>
public static class WorldSerializer
{
    /// <summary>
    /// Serialize a world to a MessagePackWriter.
    /// </summary>
    public static void SerializeWorld(ref MessagePackWriter writer, World world, ArchSerializationContext archContext)
    {
        var context = new SerializationContext();
        context.DepthStep();

        // Write as a single array structure
        writer.WriteArrayHeader(5); // baseChunkSize, baseChunkEntityCount, entityDataSlots, recycledIds, archetypes

        // Write important meta data
        writer.Write(world.BaseChunkSize);
        writer.Write(world.BaseChunkEntityCount);

        // Write entity data slots
        archContext.JaggedArrayConverter.Write(ref writer, world.GetEntityDataArray(), context);

        // Write recycled entity ids
        var recycledEntityIds = world.GetRecycledEntityIds();
        archContext.RecycledIdsConverter.Write(ref writer, recycledEntityIds, context);

        // Write archetypes as array
        writer.WriteArrayHeader(world.Archetypes.Count);
        foreach (var archetype in world)
        {
            SerializeArchetype(ref writer, archetype, archContext, context);
        }
    }

    /// <summary>
    /// Deserialize a world from a MessagePackReader.
    /// </summary>
    public static World DeserializeWorld(ref MessagePackReader reader, ArchSerializationContext archContext)
    {
        var context = new SerializationContext();
        context.DepthStep();

        var outerCount = reader.ReadArrayHeader();

        // Read important metadata
        var baseChunkSize = reader.ReadInt32();
        var baseChunkEntityCount = reader.ReadInt32();

        // Create world
        var world = World.Create(chunkSizeInBytes: baseChunkSize, minimumAmountOfEntitiesPerChunk: baseChunkEntityCount);
        archContext.World = world;
        archContext.EntityConverter.WorldId = world.Id;

        // Read entity data slots
        var slots = archContext.JaggedArrayConverter.Read(ref reader, context);

        // Read recycled entity ids
        var recycledEntityIds = archContext.RecycledIdsConverter.Read(ref reader, context);

        // Forward values to the world
        world.SetRecycledEntityIds(recycledEntityIds);
        world.SetEntityDataArray(slots);
        world.EnsureCapacity(slots.Capacity);

        // Read archetypes
        var archetypeCount = reader.ReadArrayHeader();
        var archetypes = new List<Archetype>(archetypeCount);

        for (var i = 0; i < archetypeCount; i++)
        {
            var archetype = DeserializeArchetype(ref reader, archContext, context);
            archetypes.Add(archetype);
        }

        world.SetArchetypes(archetypes);
        return world;
    }

    private static void SerializeArchetype(ref MessagePackWriter writer, Archetype archetype, ArchSerializationContext archContext, SerializationContext context)
    {
        var signature = archetype.Signature;
        var chunks = archetype.Chunks;

        context.DepthStep();
        writer.WriteArrayHeader(4); // signature, lookupArray, chunkCount, chunks

        // Write signature
        archContext.SignatureConverter.Write(ref writer, signature, context);

        // Write lookup array
        writer.WriteArrayHeader(archetype.GetLookupArray().Length);
        foreach (var val in archetype.GetLookupArray())
        {
            writer.Write(val);
        }

        // Write chunk count
        writer.Write(archetype.ChunkCount);

        // Write chunks as array
        writer.WriteArrayHeader(archetype.ChunkCount);
        for (var i = 0; i < archetype.ChunkCount; i++)
        {
            ref var chunk = ref chunks[i];
            SerializeChunk(ref writer, chunk, signature, archContext, context);
        }
    }

    private static Archetype DeserializeArchetype(ref MessagePackReader reader, ArchSerializationContext archContext, SerializationContext context)
    {
        context.DepthStep();
        var outerCount = reader.ReadArrayHeader();

        // Read signature
        var signature = archContext.SignatureConverter.Read(ref reader, context);
        archContext.CurrentSignature = signature;

        // Read lookup array
        var lookupLength = reader.ReadArrayHeader();
        var lookupArray = new int[lookupLength];
        for (var i = 0; i < lookupLength; i++)
        {
            lookupArray[i] = reader.ReadInt32();
        }
        archContext.CurrentLookupArray = lookupArray;

        // Read chunk count
        var chunkCount = reader.ReadInt32();

        // Create archetype
        var world = archContext.World!;
        var archetype = DangerousArchetypeExtensions.CreateArchetype(world.BaseChunkSize, world.BaseChunkEntityCount, signature);
        archetype.Chunks.Clear(true);
        archetype.SetCount(chunkCount - 1);
        archContext.CurrentArchetype = archetype;

        // Read chunks
        var chunksArrayCount = reader.ReadArrayHeader();
        var chunksList = new List<Chunk>(chunkCount);
        var totalEntities = 0;

        for (var i = 0; i < chunksArrayCount; i++)
        {
            var chunk = DeserializeChunk(ref reader, archContext, context);
            chunksList.Add(chunk);
            totalEntities += chunk.Count;
        }

        archetype.SetChunks(chunksList);
        archetype.SetEntities(totalEntities);
        return archetype;
    }

    private static void SerializeChunk(ref MessagePackWriter writer, Chunk chunk, Signature signature, ArchSerializationContext archContext, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(4); // size, capacity, entities, componentArrays

        // Write size and capacity
        writer.Write(chunk.Count);
        writer.Write(chunk.Capacity);

        // Write entities
        writer.WriteArrayHeader(chunk.Entities.Length);
        foreach (var entity in chunk.Entities)
        {
            archContext.EntityConverter.Write(ref writer, entity, context);
        }

        // Write component arrays
        writer.WriteArrayHeader(signature.Count);
        foreach (var type in signature.Components)
        {
            var array = chunk.GetArray(type);
            SerializeComponentArray(ref writer, array, chunk.Count, type, archContext);
        }
    }

    private static Chunk DeserializeChunk(ref MessagePackReader reader, ArchSerializationContext archContext, SerializationContext context)
    {
        var world = archContext.World!;
        var archetype = archContext.CurrentArchetype!;
        var signature = archContext.CurrentSignature;
        var lookupArray = archContext.CurrentLookupArray!;

        context.DepthStep();
        var outerCount = reader.ReadArrayHeader();

        // Read size and capacity
        var size = reader.ReadInt32();
        var capacity = reader.ReadInt32();

        // Read entities
        var entityCount = reader.ReadArrayHeader();
        var entities = new Entity[entityCount];
        for (var i = 0; i < entityCount; i++)
        {
            entities[i] = archContext.EntityConverter.Read(ref reader, context);
        }

        // Create chunk
        var chunk = DangerousChunkExtensions.CreateChunk(capacity, lookupArray, signature);
        entities.CopyTo(chunk.Entities, 0);
        chunk.SetSize(size);

        // Update entity archetype references
        for (var i = 0; i < size; i++)
        {
            ref var entity = ref chunk.Entity(i);
            entity = DangerousEntityExtensions.CreateEntityStruct(entity.Id, world.Id, entity.Version);
            world.SetArchetype(entity, archetype);
        }

        // Read component arrays
        var componentArrayCount = reader.ReadArrayHeader();
        var signatureIndex = 0;
        foreach (var type in signature.Components)
        {
            if (signatureIndex < componentArrayCount)
            {
                var array = DeserializeComponentArray(ref reader, size, type, archContext);
                var chunkArray = chunk.GetArray(type.Type);
                Array.Copy(array, chunkArray, size);
                signatureIndex++;
            }
        }

        return chunk;
    }

    private static void SerializeComponentArray(ref MessagePackWriter writer, Array array, int count, ComponentType type, ArchSerializationContext archContext)
    {
        var elementType = array.GetType().GetElementType()!;

        // Write type ID for deserialization
        var typeId = ComponentTypeRegistry.GetTypeId(elementType);

        writer.WriteArrayHeader(2); // typeId, data

        writer.Write(typeId);

        if (typeId >= 0)
        {
            // Use registered serializer
            var serializer = ComponentTypeRegistry.GetSerializer(elementType);
            serializer!.Serialize(ref writer, array, count, archContext.Serializer);
        }
        else
        {
            // Fallback: write type name and serialize as unknown
            writer.WriteArrayHeader(2);
            writer.Write(elementType.AssemblyQualifiedName ?? elementType.FullName ?? elementType.Name);
            writer.WriteArrayHeader(count);
            // Skip serialization for unregistered types - they will be default values on load
            for (var i = 0; i < count; i++)
            {
                writer.WriteNil();
            }
        }
    }

    private static Array DeserializeComponentArray(ref MessagePackReader reader, int count, ComponentType type, ArchSerializationContext archContext)
    {
        var context = new SerializationContext();
        var outerCount = reader.ReadArrayHeader();

        var typeId = reader.ReadInt32();

        if (typeId >= 0)
        {
            var elementType = ComponentTypeRegistry.GetTypeFromId(typeId);
            if (elementType != null)
            {
                var serializer = ComponentTypeRegistry.GetSerializer(elementType);
                if (serializer != null)
                {
                    return serializer.Deserialize(ref reader, count, archContext.Serializer);
                }
            }
        }

        // Fallback: read type name and skip data
        var fallbackCount = reader.ReadArrayHeader();
        if (fallbackCount >= 1 && reader.NextMessagePackType == MessagePackType.String)
        {
            _ = reader.ReadString();
        }
        if (fallbackCount >= 2)
        {
            var arrayLength = reader.ReadArrayHeader();
            for (var i = 0; i < arrayLength; i++)
            {
                reader.Skip(context);
            }
        }

        // Return empty array of the type
        return Array.CreateInstance(type.Type, count);
    }
}
