using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.datastreams;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.attributes.packets;
using System.Threading;

namespace tairasoul.unity.common.networking.clients;

[ImplementUnreliableRead]
[ImplementUnreliableHeaderWrite]
[ImplementReliableRead]
[ImplementReliableHeaderWrite]
partial class ClientUdp : IClient {
	UdpClient client;
	//Dictionary<PacketType, Action<object>[]> processors = [];
	MemoryStream serialization = new();
	LocalStream readStream = new();
	public BitReaderAsync bitReader;
	BitWriter serializedWriter;
	string host;
	int port;
	bool needsFlush = false;
	public ClientUdp(string host, int hostPort, int port) {
		client = new(port);
		client.Client.ReceiveBufferSize = 1024 * 1024;
		this.host = host;
		this.port = hostPort;
		serializedWriter = new(serialization);
		bitReader = new(readStream);
	}

	public void Disconnect() {
		ActionQueue.Enqueue(client.Close);
	}

	public void SendPacket<T>(T packet) where T : IPacket {
		ActionQueue.Enqueue(() =>
		{
			WritePacketHeader(packet, serializedWriter);
			serializedWriter.Write(packet);
			needsFlush = true;
		});
	}

	public async Task PacketReadThread()
	{
		while (true) {
			var pack = await ReadPacket(bitReader);
			if (pack == null) continue;
			if (processors.ContainsKey(pack.Value.packetType)) {
				ActionQueue.Enqueue(() =>
				{
					foreach (var processor in processors[pack.Value.packetType])
						processor(pack.Value.data);
				});
			}
		}
	}

	bool started = false;

	public async Task ProcessPackets(CancellationToken ct) {
		if (started) return;
		started = true;
		Task.Run(PacketReadThread, ct);
		while (true) {
			UdpReceiveResult recv = await client.ReceiveAsync();
			_ = readStream.WriteAsync(recv.Buffer, 0, recv.Buffer.Length);
		}
	}
}