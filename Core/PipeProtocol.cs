using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceStudio.Core;
public static class PipeProtocol
{
    public const int MaxFrame = 1_048_576;
    public static byte[] Json(object message) => Frame(0, JsonSerializer.SerializeToUtf8Bytes(message));
    public static byte[] Audio(long sampleOffset, byte[] pcm)
    {
        var payload = new byte[8 + pcm.Length];
        BinaryPrimitives.WriteInt64LittleEndian(payload, sampleOffset);
        pcm.CopyTo(payload, 8);
        return Frame(1, payload);
    }
    private static byte[] Frame(byte kind, byte[] payload)
    {
        if (payload.Length + 1 > MaxFrame) throw new InvalidDataException("Frame too large");
        var frame = new byte[5 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length + 1);
        frame[4] = kind;
        payload.CopyTo(frame, 5);
        return frame;
    }
    public static async Task<JsonElement> ReadJsonAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 2 || length > MaxFrame) throw new InvalidDataException("Invalid frame length");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        if (body[0] != 0) throw new InvalidDataException("Expected JSON event");
        using var doc = JsonDocument.Parse(body.AsMemory(1));
        return doc.RootElement.Clone();
    }
}
