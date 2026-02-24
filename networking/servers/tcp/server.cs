using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.attributes.packets;
using System.Threading;
using tairasoul.unity.common.networking.registries;
using System.Linq;

namespace tairasoul.unity.common.networking.servers;

public class TcpConnection {
	public TcpClient client;
	public Stream stream;
	public BitReaderAsync reader;
	public BitWriter writer;
	public bool needsFlush = false;
}

public partial class ServerTcp : IServer {
	Dictionary<object, Action<object, ushort>[]> processors = [];
	TcpListener listener;
	ushort currentPlayerIndex = 2;
	public Dictionary<ushort, TcpConnection> players = [];
	//Dictionary<PacketType, Action<object, ushort>[]> processors = [];
	Action<object, ushort> onConn = (_, _) => {};
	public Action<TcpClient, ushort> ConnAdded = (_, _) => { };
	public ServerTcp(int port) {
		listener = new(IPAddress.Any, port);
		listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		listener.Start();
	}

	public void Close() {
		listener.Stop();
	}

	public void RegisterPacketProcessor<T>(object type, Action<T, ushort> processor) where T : IPacket
	{
		if (processors.TryGetValue(type, out var list)) {
			processors.Add(type, [.. list, (a, b) => processor((T)a, b)]);
		}
		else {
			processors.Add(type, [(a, b) => processor((T)a, b)]);
		}
	}

	public void RelayHeader(object header, ushort player) {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(header);
		ActionQueue.Enqueue(() =>
		{
			players[player].writer.WriteInt(info.id, (uint)NetworkPacketRegistry.bitCount);
			players[player].needsFlush = true;
		});
	}

	public void RelayHeader(object packet, params ushort[] players) {
		foreach (ushort player in players) {
			RelayHeader(packet, player);
		}
	}

	public void RelayHeaderAll(object header) {
		foreach (ushort player in players.Keys) {
			RelayHeader(header, player);
		}
	}

	public void RelayHeaderExcept(object header, params ushort[] except) {
		foreach (ushort player in players.Keys) {
			if (!except.Contains(player)) {
				RelayHeader(header, player);
			}
		}
	}

	public void Relay<T>(T packet, ushort player) where T : IPacket {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
    ActionQueue.Enqueue(() =>
    {
			players[player].writer.WriteInt(info.id, (uint)NetworkPacketRegistry.bitCount);
			players[player].writer.Write(packet);
			players[player].needsFlush = true;
    });
	}

	public void RelayAll<T>(T packet) where T : IPacket {
		foreach (ushort conn in players.Keys) {
			Relay(packet, conn);
		}
	}

	public void Relay<T>(T packet, ushort[] players) where T : IPacket {
		foreach (ushort player in players) {
			Relay(packet, player);
		}
	}

	public void RelayExcept<T>(T packet, params ushort[] player) where T : IPacket {
		foreach (ushort pl in players.Keys) {
			if (!player.Contains(pl)) {
				Relay(packet, pl);
			}
		}
	}

	bool started = false;

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

	public async Task ProcessPackets(ushort id, TcpConnection conn)
	{
		while (true) {
			var pack = await ReadPacket(conn.reader);
			if (pack == null) continue;
			CheckSpecialAction(pack.Value.info.@enum, conn.reader);
			if (processors.ContainsKey(pack.Value.info.@enum)) {
				ActionQueue.Enqueue(() =>
				{
					foreach (var processor in processors[pack.Value.info.@enum])
						processor(pack.Value.data, id);
				});
			}
		}
	}

	public void ProcessConnections(CancellationToken ct)
	{
		if (started) return;
		started = true;
		while (true) {
			if (listener.Pending()) {
				TcpClient socket = listener.AcceptTcpClient();
				Task.Run(async () =>
				{
					TcpConnection connection = new()
					{
						client = socket,
						stream = socket.GetStream()
					};
					connection.reader = new(connection.stream);
					connection.writer = new(connection.stream);
					players[currentPlayerIndex] = connection;
					onConn(connection, currentPlayerIndex);
					ConnAdded(socket, currentPlayerIndex);
					currentPlayerIndex += 1;
					await ProcessPackets((ushort)(currentPlayerIndex - 1), connection);
				});
			}
		}
	}

	public void OnConnect(Action<object, ushort> action)
	{
		onConn = action;
	}
}