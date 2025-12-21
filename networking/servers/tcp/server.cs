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

namespace tairasoul.unity.common.networking.servers;

class TcpConnection {
	public TcpClient client;
	public Stream stream;
	public BitReaderAsync reader;
	public BitWriter writer;
	public bool needsFlush = false;
}

[ImplementServerRelay]
[ImplementUnreliableRead]
[ImplementUnreliableHeaderWrite]
[ImplementReliableRead]
[ImplementReliableHeaderWrite]
partial class ServerTcp : IServer {
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

	public void Relay<T>(T packet, ushort player) where T : IPacket {
    ActionQueue.Enqueue(() =>
    {
			WritePacketHeader(packet, players[player].writer);
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

	public void RelayExcept<T>(T packet, ushort player) where T : IPacket {
		foreach (ushort pl in players.Keys) {
			if (pl != player) {
				Relay(packet, pl);
			}
		}
	}

	bool started = false;

	public async Task ProcessPackets(ushort id, TcpConnection conn)
	{
		while (true) {
			var pack = await ReadPacket(conn.reader);
			if (pack == null) continue;
			DoServerRelay(pack.Value.data, id);
			if (processors.ContainsKey(pack.Value.packetType)) {
				ActionQueue.Enqueue(() =>
				{
					foreach (var processor in processors[pack.Value.packetType])
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