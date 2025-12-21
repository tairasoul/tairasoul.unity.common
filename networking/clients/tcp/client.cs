using System.IO;
using System.Net.Sockets;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.attributes.packets;
using System.Threading.Tasks;
using System.Threading;

namespace tairasoul.unity.common.networking.clients;

[ImplementUnreliableRead]
[ImplementUnreliableHeaderWrite]
[ImplementReliableRead]
[ImplementReliableHeaderWrite]
partial class ClientTcp : IClient
{
	TcpClient client;
	Stream stream;
	//Dictionary<PacketType, Action<object>[]> processors = [];
	public BitReaderAsync bitReader;
	BitWriter bitWriter;
	bool needsFlush = false;
	public ClientTcp(string host, int port) {
		client = new(host, port);
		client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		stream = client.GetStream();
		bitReader = new(stream);
		bitWriter = new(stream);
	}

	public void SendPacket<T>(T packet) where T : IPacket {
		// Plugin.Log.LogInfo($"Sending packet of type {packet.Type}");
		ActionQueue.Enqueue(() =>
		{
			WritePacketHeader(packet, bitWriter);
			bitWriter.Write(packet);
			needsFlush = true;
		});
	}

	bool started = false;

	public async Task ProcessPackets(CancellationToken ct)
	{
		if (started) return;
		started = true;
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
};