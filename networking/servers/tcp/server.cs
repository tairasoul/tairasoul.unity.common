using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using System.Threading;
using tairasoul.unity.common.networking.registries;
using System.Linq;
using tairasoul.unity.common.format;
using tairasoul.unity.common.networking.layer;
using tairasoul.unity.common.networking.protocols.tcp;

namespace tairasoul.unity.common.networking.servers;

public class ServerTcp : IServer {
	TcpListener listener;
	public IServerCoordinator Coordinator { get; set; }
	ushort currentId;

	public void Host(int port) {
		currentId = Coordinator.StartingID;
		listener = new(IPAddress.Any, port);
		listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		listener.Start();
	}

	public void Close() {
		foreach (var (id, pair) in Coordinator.Tracker.GetPairs()) {
			var conn = Coordinator.ConnectionFromPair(this, pair);
			conn.Close();
		}
		listener.Stop();
		listener = null;
	}

	void RelayHeader(object header, IConnection connection) {
		uint id = GetPacketByHeader(header);
		ActionQueue.Enqueue(() =>
		{
			connection.Writer.Write(id);
			connection.NeedsFlush = true;
		});
	}

	void RelayHeader(object header, ConnectionPair pair) {
		var conn = Coordinator.ConnectionFromPair(this, pair);
		RelayHeader(header, conn);
	}

	public void RelayHeader(object header, ushort player) {
		var pair = Coordinator.Tracker.GetPair(player);
		if (pair == null) return;
		RelayHeader(header, pair);
	}

	public void RelayHeader(object packet, params ushort[] players) {
		foreach (ushort player in players) {
			RelayHeader(packet, player);
		}
	}

	public void RelayHeaderAll(object header) {
		foreach (var (id, pair) in Coordinator.Tracker.GetPairs()) {
			RelayHeader(header, pair);
		}
	}

	public void RelayHeaderExcept(object header, params ushort[] except) {
		foreach (var (id, pair) in Coordinator.Tracker.GetPairs()) {
			if (!except.Contains(id)) {
				RelayHeader(header, pair);
			}
		}
	}

	// implemented at preload time via csmixer/mixer.cs

	uint GetPacketId<T>() {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
		return info.id;
	}

	// implemented at preload time via csmixer/mixer.cs

	uint GetPacketByHeader(object obj) {
		return uint.MaxValue;
	}

	void WriteById(FormatWriter writer, uint id, object packet) {
		writer.Write((IPacket)packet);
	}

	void Relay<T>(T packet, IConnection connection) where T : IPacket {
		uint id = GetPacketId<T>();
		ActionQueue.Enqueue(() =>
		{
			connection.Writer.Write(id);
			WriteById(connection.Writer, id, packet);
			connection.NeedsFlush = true;
		});
	}

	void Relay<T>(T packet, ConnectionPair pair) where T : IPacket {
		var conn = Coordinator.ConnectionFromPair(this, pair);
		Relay(packet, conn);
	}

	public void Relay<T>(T packet, ushort player) where T : IPacket {
		var pair = Coordinator.Tracker.GetPair(player);
		if (pair == null) return;
		Relay(packet, pair);
	}

	public void RelayAll<T>(T packet) where T : IPacket {
		foreach (var (id, pair) in Coordinator.Tracker.GetPairs()) {
			Relay(packet, pair);
		}
	}

	public void Relay<T>(T packet, ushort[] players) where T : IPacket {
		foreach (ushort player in players) {
			Relay(packet, player);
		}
	}

	public void RelayExcept<T>(T packet, params ushort[] player) where T : IPacket {
		foreach (var (id, pair) in Coordinator.Tracker.GetPairs()) {
			if (!player.Contains(id)) {
				Relay(packet, pair);
			}
		}
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

	// implemented at preload time via csmixer/mixer.cs

	bool packetIdIsValid(uint packetId) {
		return NetworkPacketRegistry.GetPacketInfo(packetId) != null;
	}

	// implemented at preload time via csmixer/mixer.cs

	void RouteExplicit(uint packetId, ushort playerId) {

	}

	void CheckRouting(object? data, uint packetId, ushort playerId) {
		if (data is IRoutedPacket routed)
			routed.Route(true, playerId);
		else
			RouteExplicit(packetId, playerId);
	}

	public async Task ProcessPackets(ushort id, IConnection conn)
	{
		while (true) {
			conn.RecheckConnected();
			if (!conn.Connected) {
				Coordinator.HandleDisconnect(this, id);
				break;
			}
			var (data, packetId) = await ReadPacket(conn.Reader);
			if (!packetIdIsValid(packetId)) continue;
			CheckRouting(data, packetId, id);
		}
	}

	public async Task ProcessConnections(CancellationToken ct)
	{
		if (started) return;
		started = true;
		while (true) {
			TcpClient socket = await listener.AcceptTcpClientAsync();
			socket.NoDelay = true;
			var conn = new TcpConnection();
			conn.Create(socket.GetStream(), socket.GetStream());
			conn.client = socket;
			Coordinator.ClientConnected(this, currentId, conn, (IPEndPoint)socket.Client.RemoteEndPoint);
			_ = Task.Run(async () =>
			{
				await ProcessPackets(currentId, conn);
			}, ct);
			currentId++;
		}
	}
}