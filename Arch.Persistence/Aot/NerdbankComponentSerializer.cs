using Nerdbank.MessagePack;
using PolyType;

namespace Arch.Persistence.Aot;

/// <summary>
/// Typed component serializer using Nerdbank.MessagePack.
/// Handles serialization of component arrays with full AOT support.
/// Uses IShapeable constraint to leverage PolyType source generation.
/// </summary>
public class NerdbankComponentSerializer<T> : IComponentSerializer where T : struct, IShapeable<T>
{
    public void Serialize(ref MessagePackWriter writer, Array array, int count, MessagePackSerializer serializer)
    {
        var typed = (T[])array;

        // Write count
        writer.WriteArrayHeader(count);

        // Write each element using the generic serializer that leverages IShapeable<T>
        for (var i = 0; i < count; i++)
        {
            serializer.Serialize(ref writer, typed[i]);
        }
    }

    public Array Deserialize(ref MessagePackReader reader, int count, MessagePackSerializer serializer)
    {
        var arrayLength = reader.ReadArrayHeader();
        var result = new T[count];

        // Read each element using the generic serializer that leverages IShapeable<T>
        for (var i = 0; i < arrayLength && i < count; i++)
        {
            result[i] = serializer.Deserialize<T>(ref reader);
        }

        return result;
    }
}
