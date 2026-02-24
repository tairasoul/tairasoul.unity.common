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
using tairasoul.unity.common.networking.registries;
using System.Linq;

namespace tairasoul.unity.common.networking.servers;

public class UdpConnection {
	public IPEndPoint addr;
	public LocalStream readMem;
	public MemoryStream writeMem;
	public BitReaderAsync reader;
	public BitWriter writer;
	public bool requiresFlush = false;
}

public partial class ServerUdp : IServer {
	Dictionary<object, Action<object, ushort>[]> processors = [];
	UdpClient client;
	public Dictionary<ushort, UdpConnection> players = [];

	public ServerUdp(int port) {
		client = new(new IPEndPoint(IPAddress.Any, port));
		client.Client.ReceiveBufferSize = 1024 * 1024;
	}

	public void Close() {
		client.Close();
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
			players[player].requiresFlush = true;
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

	public void RelayAll<T>(T packet) where T : IPacket {
		foreach (ushort player in players.Keys) {
			Relay(packet, player);
		}
	}

	public void Relay<T>(T packet, ushort player) where T : IPacket {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
		ActionQueue.Enqueue(() =>
		{
			players[player].writer.WriteInt(info.id, (uint)NetworkPacketRegistry.bitCount);
			players[player].writer.Write(packet);
			players[player].requiresFlush = true;
		});
	}

	void Relay(object packet, ushort player) {
		ActionQueue.Enqueue(() =>
		{
			NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(packet.GetType());
			players[player].writer.WriteInt(info.id, (uint)NetworkPacketRegistry.bitCount);
			if (packet != null)
				players[player].writer.Write(packet);
			players[player].requiresFlush = true;
		});
	}

	public void Relay<T>(T packet, params ushort[] players) where T : IPacket {
		foreach (ushort player in players) {
			Relay(packet, player);
		}
	}

	public void RelayExcept<T>(T packet, params ushort[] except) where T : IPacket {
		foreach (ushort pl in players.Keys) {
			if (!except.Contains(pl)) {
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

	async Task ProcessPackets(ushort id, UdpConnection conn) {
		while (true) {
			var pack = await ReadPacket(conn.reader);
			if (pack == null) continue;
			CheckSpecialAction(pack.Value.info.@enum, conn.reader);
			if (pack.Value.info.relay == ServerRelayType.RelayExceptSender) {
				foreach (ushort pl in players.Keys) {
					if (pl != id) {
						Relay(pack.Value.data, pl);
					}
				}
			}
			if (processors.ContainsKey(pack.Value.info.@enum)) {
				ActionQueue.Enqueue(() =>
				{
					foreach (var processor in processors[pack.Value.info.@enum])
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