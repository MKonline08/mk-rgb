using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using MkRgb.Core;

namespace MkRgb.Hardware;

public sealed class OpenRgb(int port = 6743) : IRgbController
{
    TcpClient? client; NetworkStream? stream; CancellationTokenSource? life;
    Task? receiver;
    readonly SemaphoreSlim gate = new(1); readonly SemaphoreSlim writes = new(1);
    readonly Channel<(uint Id, byte[] Data)> replies = Channel.CreateBounded<(uint, byte[])>(8);
    readonly Dictionary<string, (uint Index, int Count)> mapping = []; readonly HashSet<string> direct = [];
    readonly Dictionary<string, List<Zone>> zoneMapping = [];
    volatile bool dirty = true;
    public bool NeedsDiscovery => dirty;
    public async Task<List<Device>> DiscoverAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (client is null || !client.Connected)
            {
                client?.Dispose();
                life?.Cancel();
                if (receiver != null)
                    await receiver;
                life?.Dispose();
                life = new();
                client = new()
                {
                    NoDelay = true
                };
                await client.ConnectAsync("127.0.0.1", port, token);
                stream = client.GetStream();
                while (replies.Reader.TryRead(out _))
                {
                }
                receiver = Receive(client, stream, life.Token);
                var protocol = await Request(0, 40, BitConverter.GetBytes(3u), token);
                if (protocol.Length < 4 || BitConverter.ToUInt32(protocol) < 3)
                    throw new InvalidDataException("OpenRGB SDK protocol 3 or later is required.");
                await Send(0, 50, Encoding.UTF8.GetBytes("MK RGB\0"), token);
            }
            dirty = false;
            mapping.Clear();
            zoneMapping.Clear();
            direct.Clear();
            var countData = await Request(0, 0, [], token);
            if (countData.Length != 4)
                throw new InvalidDataException("Unexpected device count packet.");
            var count = BitConverter.ToUInt32(countData);
            if (count > 256)
                throw new InvalidDataException("Too many RGB controllers.");
            List<Device> devices = [];
            for (uint i = 0; i < count; i++)
            {
                var d = Parse(await Request(i, 1, BitConverter.GetBytes(3u), token));
                if (mapping.ContainsKey(d.Id))
                    throw new InvalidDataException("Two RGB devices have the same identity; reconnect them separately.");
                mapping[d.Id] = (i, d.LedCount);
                zoneMapping[d.Id] = d.Zones;
                devices.Add(d);
            }
            if (dirty)
            {
                mapping.Clear();
                throw new IOException("RGB device inventory changed during discovery. Retry discovery.");
            }
            return devices;
        }
        catch { client?.Dispose(); client = null; dirty = true; throw; }
        finally { gate.Release(); }
    }
    public async Task WriteAsync(string id, Rgb[] colors, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (dirty || !mapping.TryGetValue(id, out var d))
                throw new IOException("RGB device list changed; refresh before writing.");
            if (colors.Length != d.Count)
                throw new InvalidDataException("LED frame size does not match the device.");
            if (!direct.Contains(id))
            {
                await Send(d.Index, 1100, [], token);
                direct.Add(id);
            }
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            w.Write(6 + colors.Length * 4);
            w.Write((ushort)colors.Length);
            foreach (var c in colors)
            {
                w.Write(c.R);
                w.Write(c.G);
                w.Write(c.B);
                w.Write((byte)0);
            }
            await Send(d.Index, 1050, m.ToArray(), token);
        }
        catch { dirty = true; client?.Dispose(); throw; }
        finally { gate.Release(); }
    }
    public async Task ResizeAsync(string id, int zone, int count, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (dirty || !mapping.TryGetValue(id, out var d))
                throw new IOException("Rediscover devices first.");
            var target = zoneMapping[id].FirstOrDefault(z => z.Id == zone.ToString());
            if (target is null || !target.Resizable)
                throw new InvalidDataException("This RGB zone cannot be resized.");
            if (count < target.Minimum || count > Math.Min(10000, target.Maximum))
                throw new InvalidDataException($"LED count must be between {target.Minimum} and {Math.Min(10000, target.Maximum)} for this zone.");
            await Send(d.Index, 1000, BitConverter.GetBytes(zone).Concat(BitConverter.GetBytes(count)).ToArray(), token);
            dirty = true;
        }
        finally { gate.Release(); }
    }
    async Task<byte[]> Request(uint device, uint command, byte[] data, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(5000);
        try
        {
            await Send(device, command, data, timeout.Token);
            while (true)
            {
                var replyTask = replies.Reader.ReadAsync(timeout.Token).AsTask();
                if (await Task.WhenAny(replyTask, receiver!) != replyTask)
                    throw new IOException("OpenRGB disconnected while responding. Retry Connect RGB.");
                var reply = await replyTask;
                if (reply.Id == command) return reply.Data;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("OpenRGB did not respond within five seconds. Retry Connect RGB."); }
        finally { timeout.Cancel(); }
    }
    async Task Send(uint device, uint command, byte[] data, CancellationToken token)
    {
        await writes.WaitAsync(token);
        try
        {
            if (stream is null)
                throw new IOException("OpenRGB disconnected.");
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            w.Write(Encoding.ASCII.GetBytes("ORGB"));
            w.Write(device);
            w.Write(command);
            w.Write(data.Length);
            w.Write(data);
            await stream.WriteAsync(m.ToArray(), token);
        }
        finally { writes.Release(); }
    }
    async Task Receive(TcpClient connection, NetworkStream input, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var header = new byte[16];
                await input.ReadExactlyAsync(header, token);
                if (Encoding.ASCII.GetString(header, 0, 4) != "ORGB")
                    throw new InvalidDataException("Invalid OpenRGB header.");
                var id = BitConverter.ToUInt32(header, 8);
                var length = BitConverter.ToInt32(header, 12);
                if (length < 0 || length > 4_000_000)
                    throw new InvalidDataException("Invalid OpenRGB packet length.");
                var data = new byte[length];
                await input.ReadExactlyAsync(data, token);
                if (id == 100)
                {
                    dirty = true;
                    continue;
                }
                if (id is 0 or 1 or 40)
                    await replies.Writer.WriteAsync((id, data), token);
            }
        }
        catch (Exception) when (!token.IsCancellationRequested) { dirty = true; connection.Dispose(); }
        catch (Exception) when (token.IsCancellationRequested) { }
    }
    public static Device Parse(byte[] data)
    {
        using var memory = new MemoryStream(data);
        using var r = new BinaryReader(memory);
        void Skip(int n)
        {
            if (n < 0 || memory.Position + n > memory.Length)
                throw new InvalidDataException("Truncated RGB description.");
            memory.Position += n;
        }
        string Str()
        {
            var n = r.ReadUInt16();
            var bytes = r.ReadBytes(n);
            if (bytes.Length != n)
                throw new InvalidDataException("Truncated RGB string.");
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }
        var size = r.ReadUInt32();
        if (size != data.Length)
            throw new InvalidDataException("Invalid RGB description size.");
        _ = r.ReadUInt32();
        var name = Str();
        var vendor = Str();
        _ = Str();
        _ = Str();
        var serial = Str();
        var location = Str();
        var modes = r.ReadUInt16();
        Skip(4);
        for (var i = 0; i < modes; i++)
        {
            _ = Str();
            Skip(48);
            var n = r.ReadUInt16();
            Skip(n * 4);
        }
        var zoneCount = r.ReadUInt16();
        List<Zone> zones = [];
        var offset = 0;
        for (var i = 0; i < zoneCount; i++)
        {
            var zoneName = Str();
            _ = r.ReadUInt32();
            var min = r.ReadUInt32();
            var max = r.ReadUInt32();
            var n = checked((int)r.ReadUInt32());
            var matrix = r.ReadUInt16();
            Skip(matrix);
            zones.Add(new(i.ToString(), zoneName, offset, n, min != max, checked((int)min), checked((int)max)));
            offset += n;
        }
        var leds = r.ReadUInt16();
        for (var i = 0; i < leds; i++)
        {
            _ = Str();
            Skip(4);
        }
        var colors = r.ReadUInt16();
        Skip(colors * 4);
        if (memory.Position != memory.Length || colors != leds || offset != leds)
            throw new InvalidDataException("Inconsistent RGB zone, LED, or color data.");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(vendor + "|" + name + "|" + serial + "|" + location)))[..24];
        return new("openrgb:" + identity, name, vendor, Capability.Rgb, colors, zones, "Upstream supported; physical validation pending", false);
    }
    public async ValueTask DisposeAsync()
    {
        life?.Cancel();
        client?.Dispose();
        if (receiver != null)
            await receiver;
        life?.Dispose();
    }
}
