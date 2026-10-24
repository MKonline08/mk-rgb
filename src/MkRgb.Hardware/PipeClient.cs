using System.IO.Pipes;
using System.Text;
using MkRgb.Core;

namespace MkRgb.Hardware;

public static class PipeClient
{
    public const string Name = "MkRgb.Hardware.v1";
    public static async Task<T> Call<T>(string method, object? payload = null, int timeout = 3000, CancellationToken token = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        await using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(limit.Token);
        await Framing.Write(pipe, Json.Write(new RpcRequest(method, payload is null ? "" : Json.Write(payload))), limit.Token);
        var response = Json.Read<RpcResponse>(await Framing.Read(pipe, limit.Token));
        if (!response.Success)
            throw new InvalidOperationException(response.Error);
        return Json.Read<T>(response.Payload);
    }
}
public static class Framing
{
    public static async Task Write(Stream stream, string value, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > 4_000_000)
            throw new InvalidDataException("Message too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<string> Read(Stream stream, CancellationToken token)
    {
        var size = new byte[4];
        await stream.ReadExactlyAsync(size, token);
        var n = BitConverter.ToInt32(size);
        if (n < 0 || n > 4_000_000)
            throw new InvalidDataException("Invalid message length.");
        var data = new byte[n];
        await stream.ReadExactlyAsync(data, token);
        return Encoding.UTF8.GetString(data);
    }
}
