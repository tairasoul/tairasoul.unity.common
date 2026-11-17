using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.datastreams;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using System.Threading;
using tairasoul.unity.common.networking.registries;
using System;
using System.Collections.Generic;
using tairasoul.unity.common.format;
using tairasoul.unity.common.networking.layer;
using tairasoul.unity.common.networking.protocols.udp;
using System.Net;

namespace tairasoul.unity.common.networking.clients;

public class ClientUdp : IClient {
	UdpConnection connection;
	public int ourPort;
	// bool isConnected = false;
	public ClientUdp() {
		connection = new();
	}

	public void Disconnect() {
		connection.Close();
		connection.client.Close();
		connection.Connected = false;
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

	public async Task PacketReadThread()
	{
		while (true) {
			var (data, packetId) = await ReadPacket(connection.Reader);
			if (!packetIdIsValid(packetId)) continue;
			CheckRouting(data, packetId);
		}
	}

	bool started = false;

	public async Task ProcessPackets(CancellationToken ct) {
		if (started) return;
		started = true;
		_ = Task.Run(PacketReadThread, ct);
		while (true) {
			UdpReceiveResult recv = await connection.client.ReceiveAsync();
			_ = connection.readMem.WriteAsync(recv.Buffer, 0, recv.Buffer.Length);
		}
	}
	
	public void Flush()
	{
		connection.Flush();
	}

	public void Connect(string host, int port)
	{
		connection.addr = new(IPAddress.Parse(host), port);
		connection.client = new(ourPort);
		connection.client.Client.ReceiveBufferSize = 1024 * 1024;
		connection.Connected = true;
	}
}