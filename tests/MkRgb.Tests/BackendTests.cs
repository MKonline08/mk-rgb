using System.Net;
using System.Net.Sockets;
using System.Text;
using MkRgb.Hardware;

namespace MkRgb.Tests;

public class BackendTests
{
    [Fact]
    public async Task RgbReconnectsAfterServerDisconnectAndInvalidatesOnHotplug()
    {
        await using var server = new FixtureServer();
        await using var client = new OpenRgb(server.Port);
        Assert.Empty(await client.DiscoverAsync(CancellationToken.None));
        server.Connection!.Dispose();
        await WaitUntil(() => client.NeedsDiscovery);
        Assert.Empty(await client.DiscoverAsync(CancellationToken.None));
        Assert.Equal(2, server.Connections);
        await FixtureServer.Packet(server.Connection!.GetStream(), 100, []);
        await WaitUntil(() => client.NeedsDiscovery);
        await Assert.ThrowsAsync<IOException>(() => client.WriteAsync("removed", [], CancellationToken.None));
    }
    [Fact]
    public async Task DuplicateControllerIdentityRejectsDiscovery()
    {
        await using var server = new FixtureServer(duplicate: true);
        await using var client = new OpenRgb(server.Port);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DiscoverAsync(CancellationToken.None));
        Assert.True(client.NeedsDiscovery);
    }
    static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }
    sealed class FixtureServer : IAsyncDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly Task run;
        readonly bool duplicate;
        public TcpClient? Connection { get; private set; }
        public int Connections { get; private set; }
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public FixtureServer(bool duplicate = false) { this.duplicate = duplicate; listener.Start(); run = Run(); }
        async Task Run()
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var socket = await listener.AcceptTcpClientAsync(stop.Token);
                    Connection = socket; Connections++;
                    using var stream = socket.GetStream();
                    while (!stop.IsCancellationRequested)
                    {
                        var header = new byte[16]; await stream.ReadExactlyAsync(header, stop.Token);
                        var id = BitConverter.ToUInt32(header, 8);
                        var body = new byte[BitConverter.ToInt32(header, 12)]; await stream.ReadExactlyAsync(body, stop.Token);
                        if (id == 40) await Packet(stream, id, BitConverter.GetBytes(3u));
                        if (id == 0) await Packet(stream, id, BitConverter.GetBytes(duplicate ? 2u : 0u));
                        if (id == 1) await Packet(stream, id, Device());
                    }
                }
                catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
            }
        }
        public static async Task Packet(NetworkStream stream, uint id, byte[] payload)
        {
            using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
            writer.Write(Encoding.ASCII.GetBytes("ORGB")); writer.Write(0u); writer.Write(id); writer.Write(payload.Length); writer.Write(payload);
            await stream.WriteAsync(memory.ToArray());
        }
        static byte[] Device()
        {
            using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
            writer.Write(0u); writer.Write(0u);
            foreach (var s in new[] { "Fixture", "Test vendor", "Description", "1", "identical serial", "identical location" }) { var bytes = Encoding.UTF8.GetBytes(s + '\0'); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
            writer.Write((ushort)0); writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)0);
            memory.Position = 0; writer.Write((uint)memory.Length); return memory.ToArray();
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); Connection?.Dispose(); listener.Stop(); await run; stop.Dispose(); }
    }
}
