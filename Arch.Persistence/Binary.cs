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
/// Writes name-based format: [count, [typeName, byteSize], ...]
/// Reading is handled inline in DeserializeArchetype for access to ArchSerializationContext.
/// </summary>
public class SignatureConverter : MessagePackConverter<Signature>
{
    public override Signature Read(ref MessagePackReader reader, SerializationContext context)
    {
        // Name-based reading is handled in DeserializeArchetype directly
        // to access ArchSerializationContext for tracking unknown types.
        // This method exists only for the MessagePackConverter contract.
        throw new InvalidOperationException("Signature reading is handled inline in DeserializeArchetype");
    }

    public override void Write(ref MessagePackWriter writer, in Signature value, SerializationContext context)
    {
        context.DepthStep();
        writer.WriteArrayHeader(value.Count);
        foreach (var type in value.Components)
        {
            writer.WriteArrayHeader(2);
            writer.Write(type.Type?.FullName
                ?? throw new InvalidOperationException($"ComponentType ID {type.Id} has no resolved Type during serialization"));
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
/// Tracks a saved signature entry: the type name from the save file, its byte size,
/// and the resolved ComponentType if the type exists in the current build.
/// </summary>
public readonly struct SavedSignatureEntry
{
    public readonly string Name;
    public readonly int ByteSize;
    public readonly ComponentType? Resolved;

    public SavedSignatureEntry(string name, int byteSize, ComponentType? resolved)
    {
        Name = name;
        ByteSize = byteSize;
        Resolved = resolved;
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

    /// <summary>
    /// Full list of saved signature entries for the current archetype.
    /// Tracks all saved component types (resolved and unknown) so chunk
    /// deserialization can iterate in the correct order and skip unknowns.
    /// </summary>
    public List<SavedSignatureEntry> SavedSignatureEntries { get; set; } = new();

    public int[]? CurrentLookupArray { get; set; }

    public ArchSerializationContext() : this(Array.Empty<MessagePackConverter>())
    {
    }

    public ArchSerializationContext(IEnumerable<MessagePackConverter> additionalConverters)
    {
        // Create serializer with custom converters using the 'with' pattern
        var serializer = new MessagePackSerializer();

        // Add all converters
        // SignatureConverter is NOT registered here — it's only called directly
        // via archContext.SignatureConverter.Write() for serialization. Deserialization
        // is handled inline in DeserializeArchetype for name-based type resolution.
        var allConverters = new List<MessagePackConverter>
        {
            EntityConverter,
            ComponentTypeConverter,
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

        // Write signature using name-based format
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

        // Read signature entries using name-based format (inline, not via SignatureConverter)
        var sigCount = reader.ReadArrayHeader();
        var savedEntries = new List<SavedSignatureEntry>(sigCount);
        var knownTypes = new List<ComponentType>(sigCount);

        for (var i = 0; i < sigCount; i++)
        {
            var entryCount = reader.ReadArrayHeader();
            var name = reader.ReadString()!;
            var byteSize = reader.ReadInt32();

            var type = ComponentTypeRegistry.GetTypeFromName(name);
            ComponentType? resolved = null;
            if (type != null)
            {
                // Implicit cast auto-registers with Arch's ComponentRegistry if needed
                resolved = (ComponentType)type;
                knownTypes.Add(resolved.Value);
            }

            savedEntries.Add(new SavedSignatureEntry(name, byteSize, resolved));
        }

        var signature = new Signature(knownTypes.ToArray());
        archContext.CurrentSignature = signature;
        archContext.SavedSignatureEntries = savedEntries;

        // Read and discard the saved lookup array (encoded with old Arch IDs).
        // We'll use the fresh lookup array from the archetype created below.
        var lookupLength = reader.ReadArrayHeader();
        for (var i = 0; i < lookupLength; i++)
        {
            reader.ReadInt32();
        }

        // Read chunk count
        var chunkCount = reader.ReadInt32();

        // Create archetype with filtered signature (only known types)
        var world = archContext.World!;
        var archetype = DangerousArchetypeExtensions.CreateArchetype(world.BaseChunkSize, world.BaseChunkEntityCount, signature);
        archetype.Chunks.Clear(true);
        archetype.SetCount(chunkCount - 1);
        archContext.CurrentArchetype = archetype;

        // Use the fresh lookup array from the archetype (based on current runtime IDs)
        archContext.CurrentLookupArray = archetype.GetLookupArray();

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

        // Fix Count to point to the last chunk with entities.
        // SetCount(chunkCount - 1) above assumes the last chunk is active, but it may be empty
        // if entities were removed before saving. CurrentChunk (Chunks[Count]) must have entities,
        // otherwise Archetype.Remove crashes accessing index -1 in an empty chunk.
        var lastActive = 0;
        for (var i = chunksList.Count - 1; i >= 0; i--)
        {
            if (chunksList[i].Count > 0)
            {
                lastActive = i;
                break;
            }
        }
        archetype.SetCount(lastActive);

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

        // Read component arrays — iterate over saved signature entries to consume all data,
        // but only copy arrays for types the current build recognizes.
        var componentArrayCount = reader.ReadArrayHeader();
        var savedEntries = archContext.SavedSignatureEntries;
        for (var ci = 0; ci < componentArrayCount && ci < savedEntries.Count; ci++)
        {
            var entry = savedEntries[ci];
            var array = DeserializeComponentArray(ref reader, size, entry, archContext, context);

            // Only copy to chunk if this type is known and resolved
            if (entry.Resolved != null && array != null)
            {
                var chunkArray = chunk.GetArray(entry.Resolved.Value);
                Array.Copy(array, chunkArray, size);
            }
        }

        return chunk;
    }

    private static void SerializeComponentArray(ref MessagePackWriter writer, Array array, int count, ComponentType type, ArchSerializationContext archContext)
    {
        var elementType = array.GetType().GetElementType()!;

        var typeName = ComponentTypeRegistry.GetTypeName(elementType) ?? elementType.FullName
            ?? throw new InvalidOperationException($"Cannot resolve type name for {elementType}");

        writer.WriteArrayHeader(2); // typeName, data
        writer.Write(typeName);

        var serializer = ComponentTypeRegistry.GetSerializer(elementType);
        if (serializer != null)
        {
            // Use registered serializer
            serializer.Serialize(ref writer, array, count, archContext.Serializer);
        }
        else
        {
            // Fallback: serialize as nil array for unregistered types
            writer.WriteArrayHeader(count);
            for (var i = 0; i < count; i++)
            {
                writer.WriteNil();
            }
        }
    }

    private static Array? DeserializeComponentArray(ref MessagePackReader reader, int count, SavedSignatureEntry entry, ArchSerializationContext archContext, SerializationContext context)
    {
        var outerCount = reader.ReadArrayHeader();

        var typeName = reader.ReadString();

        // Try to find a registered serializer for this type
        if (entry.Resolved != null)
        {
            var resolvedType = entry.Resolved.Value.Type;
            if (resolvedType != null)
            {
                var serializer = ComponentTypeRegistry.GetSerializer(resolvedType);
                if (serializer != null)
                {
                    return serializer.Deserialize(ref reader, count, archContext.Serializer);
                }
            }
        }

        // Unknown or unregistered type — skip the data
        // Could be a nil array (from fallback serialization) or structured data
        if (reader.NextMessagePackType == MessagePackType.Array)
        {
            var arrayLength = reader.ReadArrayHeader();
            for (var i = 0; i < arrayLength; i++)
            {
                reader.Skip(context);
            }
        }
        else
        {
            // Single value or other format — skip it
            reader.Skip(context);
        }

        return null;
    }
}
