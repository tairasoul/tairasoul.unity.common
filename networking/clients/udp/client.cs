using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.datastreams;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using System.Threading;
using tairasoul.unity.common.networking.registries;
using System;
using System.Collections.Generic;

namespace tairasoul.unity.common.networking.clients;

public partial class ClientUdp : IClient {
	UdpClient client;
	Dictionary<object, Action<object>[]> processors = [];
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
			NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
			serializedWriter.WriteInt(info.id, (uint)NetworkPacketRegistry.bitCount);
			serializedWriter.Write(packet);
			needsFlush = true;
		});
	}

	async Task<(object? data, NetworkPacketInfo info)?> ReadPacket(BitReaderAsync reader) {
		int id = await reader.ReadInt((uint)NetworkPacketRegistry.bitCount);
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(id);
		if (info == null) return null;
		if (info.assocType != null) {
			object value = await SerdeRegistry.Deserialize(reader, info.assocType);
			return (value, info);
		}
		else {
			return (null, info);
		}
	}

	public async Task PacketReadThread()
	{
		while (true) {
			var pack = await ReadPacket(bitReader);
			if (pack == null) continue;
			if (processors.ContainsKey(pack.Value.info.@enum)) {
				ActionQueue.Enqueue(() =>
				{
					foreach (var processor in processors[pack.Value.info.@enum])
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

	public void RegisterPacketProcessor<T>(object type, Action<T> processor) where T : IPacket
	{
		if (processors.TryGetValue(type, out var list)) {
			processors.Add(type, [.. list, (o) => processor((T)o)]);
		}
		else {
			processors.Add(type, [(o) => processor((T)o)]);
		}
	}
}