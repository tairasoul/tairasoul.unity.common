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
using tairasoul.unity.common.networking.protocols.tcp;

namespace tairasoul.unity.common.networking.clients;

public partial class ClientTcp : IClient
{
	TcpConnection connection;
	public ClientTcp() {
		connection = new();
	}

	public void Disconnect() {
		connection.Close();
	}

	uint GetPacketId<T>() {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
		return info.id;
	}

	uint GetPacketByHeader(object obj) {
		return uint.MaxValue;
	}

	void WriteById(FormatWriter writer, uint id, object packet) {
		writer.Write((IPacket)packet);
	}

	public void SendPacket<T>(T packet) where T : IPacket {
		uint id = GetPacketId<T>();
		ActionQueue.Enqueue(() =>
		{
			connection.Writer.Write(id);
			WriteById(connection.Writer, id, packet);
			connection.NeedsFlush = true;
		});
	}

	public void SendPacketHeader(object packet) {
		uint id = GetPacketByHeader(packet);
		ActionQueue.Enqueue(() =>
		{
			connection.Writer.Write(id);
			connection.NeedsFlush = true;
		});
	}

	bool started = false;

	// fallback implementation, csmixer/mixer.cs replaces this with a call to a runtime-generated id to direct call method

	Task<object?> DeserializeAsync(FormatReader reader, uint id) {
		var info = NetworkPacketRegistry.GetPacketInfo(id);
		if (info == null || info.assocType == null) return Task.FromResult<object?>(null);
		return FormatRegistry.DeserializeAsync(info.assocType, reader);
	}

	async Task<(object? data, uint packetId)> ReadPacket(FormatReader reader) {
		uint id = await reader.ReadUIntAsync();
		object? value = await DeserializeAsync(reader, id);
		return (value, id);
	}

	bool packetIdIsValid(uint packetId) {
		return NetworkPacketRegistry.GetPacketInfo(packetId) != null;
	}

	void RouteExplicit(uint packetId) {

	}

	void CheckRouting(object? data, uint packetId) {
		if (data is IRoutedPacket routed)
			routed.Route(false, 0);
		else
			RouteExplicit(packetId);
	}

	public async Task ProcessPackets(CancellationToken ct)
	{
		if (started) return;
		started = true;
		while (true) {
			var (data, packetId) = await ReadPacket(connection.Reader);
			if (!packetIdIsValid(packetId)) continue;
			CheckRouting(data, packetId);
		}
	}
	
	public void Flush()
	{
		connection.Flush();
	}

	public void Connect(string host, int port)
	{
		connection.Connect(host, port);
	}
};