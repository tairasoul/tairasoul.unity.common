using System.IO;
using System.Net.Sockets;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using System.Threading.Tasks;
using System.Threading;
using tairasoul.unity.common.networking.registries;
using System.Collections.Generic;
using System;
using System.Reflection.Emit;
using tairasoul.unity.common.format;

namespace tairasoul.unity.common.networking.clients;

public partial class ClientTcp : IClient
{
	TcpClient client;
	Stream stream;
	Dictionary<object, Action<object>[]> processors = [];
	public FormatReader reader;
	FormatWriter writer;
	bool needsFlush = false;
	public ClientTcp(string host, int port) {
		client = new(host, port);
		client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		stream = client.GetStream();
		reader = new(stream, 4096);
		writer = new(4096*2);
	}

	public void SendPacketHeader(object header) {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(header);
		ActionQueue.Enqueue(() =>
		{
			writer.Write(info.id);
			needsFlush = true;
		});
	}

	public void SendPacket<T>(T packet) where T : IPacket {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
		ActionQueue.Enqueue(() =>
		{
			writer.Write(info.id);
			writer.Write(packet);
			needsFlush = true;
		});
	}

	bool started = false;

	async Task<(object? data, NetworkPacketInfo info)?> ReadPacket(FormatReader reader) {
		uint id = await reader.ReadUIntAsync();
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(id);
		if (info == null) return null;
		if (info.assocType != null) {
			object value = await FormatRegistry.DeserializeAsync(info.assocType, reader);
			return (value, info);
		}
		else {
			return (null, info);
		}
	}

	public async Task ProcessPackets(CancellationToken ct)
	{
		if (started) return;
		started = true;
		while (true) {
			var pack = await ReadPacket(reader);
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

	public void RegisterPacketProcessor<T>(object type, Action<T> processor) where T : IPacket
	{
		if (processors.TryGetValue(type, out var list)) {
			processors.Add(type, [.. list, (o) => processor((T)o)]);
		}
		else {
			processors.Add(type, [(o) => processor((T)o)]);
		}
	}
};