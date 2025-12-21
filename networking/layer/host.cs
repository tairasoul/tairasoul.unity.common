using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.servers;
using tairasoul.unity.common.networking.sync;
using static tairasoul.unity.common.networking.util.ObjectIdUtils;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.factories;
using tairasoul.unity.common.networking.attributes.packets;

namespace tairasoul.unity.common.networking.layer;

class Clients {
	public IClient reliable;
	public IClient unreliable;
}

class Servers {
	public IServer reliable;
	public IServer unreliable;
}

[ImplementReliabilityGet]
partial class HostBasedP2P : INetworkLayer {
	[MemberNotNullWhen(false, nameof(clients))]
	[MemberNotNullWhen(true, nameof(servers))]
	public bool isHost { get; set; }
	public Clients? clients;
	public Servers? servers;
	public Dictionary<ushort, string> players = [];
	public ushort playerId;
	bool written = false;
	ITransportFactory transportFactory;
	CancellationTokenSource cts = new();
	int unreliablePort;
	string username;

	public void Flush() {
		if (!written) return;
		written = false;
		if (isHost) {
			servers.reliable.Flush();
			servers.unreliable.Flush();
		}
		else {
			clients.reliable.Flush();
			clients.unreliable.Flush();
		}
	}

	public bool IsHost() => isHost;

	void ReceiveReliable(CancellationToken ct) {
		if (isHost) {
			servers.reliable.ProcessConnections(ct);
		}
		else {
			clients.reliable.ProcessPackets(ct);
		}
	}

	void ReceiveUnreliable(CancellationToken ct) {
		if (isHost) {
			servers.unreliable.ProcessConnections(ct);
		}
		else {
			clients.unreliable.ProcessPackets(ct);
		}
	}

	public void ReceiveLoop()
	{
		Task.Run(() => ReceiveReliable(cts.Token), cts.Token);
		Task.Run(() => ReceiveUnreliable(cts.Token), cts.Token);
	}

	public void Synchronize()
	{
		foreach (BaseOwnedSyncComponent c in BaseOwnedSyncComponent.ActiveNetworked.Where(c => ExtractPlayerID(c.objectId) == playerId))
			c.Synchronize();
		PlayerSyncComponent.ours?.Synchronize();
	}

	public void Disconnect()
	{
		sentConnect = false;
		cts.Cancel();
		if (isHost) {
			servers.reliable.Close();
			servers.unreliable.Close();
			servers = null;
		}
		else {
			clients.reliable.Disconnect();
			clients.unreliable.Disconnect();
			clients = null;
		}
	}

	bool sentConnect = false;

	public ushort GetPlayerID() => playerId;

	public (string username, ushort id)[] GetPlayers()
	{
		(string username, ushort id)[] tuples = [(username, 1)];
		foreach (var pair in players) {
			tuples = [.. tuples, (pair.Value, pair.Key)];
		}
		return tuples;
	}

	public void SendPacket<T>(T data, ushort id) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability(data);
		SendPacket(data, id, rel);
	}

	public void SendPacket<T>(T data, ushort[] ids) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability(data);
		SendPacket(data, ids, rel);
	}

	public void SendPacket<T>(T data) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability(data);
		SendPacket(data, rel);
	}

	ulong localCounter = 0;

	public ulong IncrementAndGetLocalCounter()
	{
		ulong counter = ++localCounter;
		return CreateID(playerId, counter);
	}

	public void SendPacket<T>(T data, ushort id, PacketReliability reliability) where T : IPacket
	{
		if (isHost)
		{
			if (reliability == PacketReliability.Reliable)
			{
				servers.reliable.Relay(data, id);
			}
			else {
				servers.unreliable.Relay(data, id);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable)
			{
				clients.reliable.SendPacket(data);
			}
			else
			{
				clients.unreliable.SendPacket(data);
			}
		}
		written = true;
	}

	public void SendPacket<T>(T data, ushort[] ids, PacketReliability reliability) where T : IPacket
	{
		if (isHost)
		{
			if (reliability == PacketReliability.Reliable)
			{
				servers.reliable.Relay(data, ids);
			}
			else {
				servers.unreliable.Relay(data, ids);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable)
			{
				clients.reliable.SendPacket(data);
			}
			else
			{
				clients.unreliable.SendPacket(data);
			}
		}
		written = true;
	}

	public void SendPacket<T>(T data, PacketReliability reliability) where T : IPacket
	{
		if (isHost)
		{
			if (reliability == PacketReliability.Reliable)
			{
				servers.reliable.RelayAll(data);
			}
			else {
				servers.unreliable.RelayAll(data);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable)
			{
				clients.reliable.SendPacket(data);
			}
			else
			{
				clients.unreliable.SendPacket(data);
			}
		}
		written = true;
	}
}