using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.datastreams;
using tairasoul.unity.common.util;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.attributes.packets;
using System.Threading;

namespace tairasoul.unity.common.networking.servers;

class UdpConnection {
	public IPEndPoint addr;
	public LocalStream readMem;
	public MemoryStream writeMem;
	public BitReaderAsync reader;
	public BitWriter writer;
	public bool requiresFlush = false;
}

[ImplementServerRelay]
[ImplementUnreliableRead]
[ImplementUnreliableHeaderWrite]
[ImplementReliableRead]
[ImplementReliableHeaderWrite]
partial class ServerUdp : IServer {
	UdpClient client;
	public Dictionary<ushort, UdpConnection> players = [];

	public ServerUdp(int port) {
		client = new(new IPEndPoint(IPAddress.Any, port));
		client.Client.ReceiveBufferSize = 1024 * 1024;
	}

	public void Close() {
		client.Close();
	}

	public void RelayAll<T>(T packet) where T : IPacket {
		foreach (ushort player in players.Keys) {
			Relay(packet, player);
		}
	}

	public void Relay<T>(T packet, ushort player) where T : IPacket {
		ActionQueue.Enqueue(() =>
		{
			WritePacketHeader(packet, players[player].writer);
			players[player].writer.Write(packet);
			players[player].requiresFlush = true;
		});
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

	public void TcpDisc(ushort id) {
		players.Remove(id);
	}

	bool started = false;

	CancellationToken ct;

	public void TcpConn(IPEndPoint endPoint, ushort id) {
		Task.Run(() =>
		{
			UdpConnection conn = new()
			{
				addr = endPoint,
				writeMem = new(),
				readMem = new()
			};
			conn.reader = new(conn.readMem);
			conn.writer = new(conn.writeMem);
			players[id] = conn;
			Task.Run(async () => await ProcessPackets(id, conn), ct);
		}, ct);
	}

	async Task ProcessPackets(ushort id, UdpConnection conn) {
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

	ushort GetConnectionID(IPEndPoint endpoint) {
		foreach (var pair in players) {
			if (pair.Value.addr.Address.Equals(endpoint.Address)) {
				return pair.Key;
			}
		}
		return 0;
	}

	public async void ProcessConnections(CancellationToken ct) {
		if (started) return;
		started = true;
		this.ct = ct;
		while (true) {
			UdpReceiveResult receiveResult = await client.ReceiveAsync();
			ushort cid = GetConnectionID(receiveResult.RemoteEndPoint);
			if (cid == 0) continue;
			// Plugin.Log.LogInfo($"got data from address {a}, client id {cid}");
			UdpConnection conn = players[cid];
			//if (!conn.addr.Equals(a)) conn.addr = a;
			_ = conn.readMem.WriteAsync(receiveResult.Buffer, 0, receiveResult.Buffer.Length);
		}
	}

	public void OnConnect(Action<object, ushort> action)
	{
		throw new NotImplementedException();
	}
}