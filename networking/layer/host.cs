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
using tairasoul.unity.common.networking.registries;
using System;

namespace tairasoul.unity.common.networking.layer;

public class Clients {
	public IClient reliable;
	public IClient unreliable;
}

public class Servers {
	public IServer reliable;
	public IServer unreliable;
}

public partial class HostBasedP2P : INetworkLayer {
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

	public void OnPacket<T>(object type, Action<T, ushort> listener) where T : IPacket
	{
		if (isHost) {
			servers.reliable.RegisterPacketProcessor(type, listener);
			servers.unreliable.RegisterPacketProcessor(type, listener);
		}
		else {
			clients.reliable.RegisterPacketProcessor<T>(type, (a) => listener(a, 0));
			clients.unreliable.RegisterPacketProcessor<T>(type, (a) => listener(a, 0));
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

	PacketReliability GetPacketReliability<T>() where T : IPacket {
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo<T>();
		if (info.reliability == null) {
			throw new InvalidOperationException($"Cannot automatically determine reliability for packet {typeof(T)}.");
		}
		return info.reliability.Value;
	}

	PacketReliability GetPacketReliability(object header)
	{
		NetworkPacketInfo info = NetworkPacketRegistry.GetPacketInfo(header);
		if (info.reliability == null)
		{
			throw new InvalidOperationException($"Cannot automatically determine reliability for packet {header}.");
		}
		return info.reliability.Value;
	}

	public void SendPacket<T>(T data, ushort id) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability<T>();
		SendPacket(data, rel, id);
	}

	public void SendPacket<T>(T data, ushort[] ids) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability<T>();
		SendPacket(data, rel, ids);
	}

	public void SendPacket<T>(T data) where T : IPacket
	{
		PacketReliability rel = GetPacketReliability<T>();
		SendPacket(data, rel);
	}

	ulong localCounter = 0;

	public ulong IncrementAndGetLocalCounter()
	{
		return ++localCounter;
	}

	public void SendPacket<T>(T data, PacketReliability reliability, ushort id) where T : IPacket
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

	public void SendPacket<T>(T data, PacketReliability reliability, ushort[] ids) where T : IPacket
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

	public void SendPacketHeader(object header, ushort id)
	{
		PacketReliability reliability = GetPacketReliability(header);
		SendPacketHeader(header, reliability, id);
	}

	public void SendPacketHeader(object header, params ushort[] ids)
	{
		PacketReliability reliability = GetPacketReliability(header);
		SendPacketHeader(header, reliability, ids);
	}

	public void SendPacketHeader(object header)
	{
		PacketReliability reliability = GetPacketReliability(header);
		SendPacketHeader(header, reliability);
	}

	public void SendPacketHeader(object header, PacketReliability reliability, ushort id)
	{
		if (isHost) {
			if (reliability == PacketReliability.Reliable) {
				servers.reliable.RelayHeader(header, id);
			}
			else {
				servers.unreliable.RelayHeader(header, id);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable) {
				
			}
			else {

			}
		}
	}

	public void SendPacketHeader(object header, PacketReliability reliability, params ushort[] ids)
	{
		if (isHost) {
			if (reliability == PacketReliability.Reliable) {
				servers.reliable.RelayHeader(header, ids);
			}
			else {
				servers.unreliable.RelayHeader(header, ids);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable) {
				
			}
			else {
				
			}
		}
	}

	public void SendPacketHeader(object header, PacketReliability reliability)
	{
		if (isHost) {
			if (reliability == PacketReliability.Reliable) {
				servers.reliable.RelayHeaderAll(header);
			}
			else {
				servers.unreliable.RelayHeaderAll(header);
			}
		}
		else {
			if (reliability == PacketReliability.Reliable) {
				
			}
			else {
				
			}
		}
	}
}