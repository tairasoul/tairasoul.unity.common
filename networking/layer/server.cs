using System;
using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.layer;

public class ServerLayer : IServerLayer
{
	public PlayerTracker Tracker { get; set; }
	public ushort StartingID { get; set; } = 0;
	public IConnectionFactory ConnectionFactory { get; set; }
	public IServer Reliable { get; set; }
	public IServer Unreliable { get; set; }
	bool NeedsSetup = true;
	public event Action<IServer, ushort> HandleDisconnect = (_, _) => { };
	public event Action<IServer, ushort> HandleConnect = (_, _) => { };

	public void PlayerDisconnect(IServer server, ushort id) {
		if (!Tracker.HasPair(id)) return;
		HandleDisconnect(server, id);
		Tracker.Untrack(id);
	}

	public void PlayerConnect(IServer server, ushort id) {
		HandleConnect(server, id);
	}

	public void Close()
	{
		if (Reliable == Unreliable) {
			Reliable.Close();
		}
		else {
			Reliable.Close();
			Unreliable.Close();
		}
		Tracker.UntrackAll();
	}

	public IConnection ConnectionFromPair(IServer server, ConnectionPair pair)
	{
		if (server == Reliable) {
			return pair.Reliable;
		}
		else {
			return pair.Unreliable!;
		}
	}

	public void Flush()
	{
		foreach (var pair in Tracker.GetPairs()) {
			pair.pair.Flush();
		}
	}

	public void Setup() {
		if (!NeedsSetup) return;
		Reliable = ConnectionFactory.ReliableServer();
		Unreliable = ConnectionFactory.UnreliableServer(Reliable);
		var coordinator = ConnectionFactory.CreateCoordinator(this);
		Reliable.Coordinator = coordinator;
		Unreliable.Coordinator = coordinator;
	}

	public void Cleanup() {
		Close();
		Reliable = null;
		Unreliable = null;
		NeedsSetup = true;
	}

	public void Host(int port)
	{
		if (NeedsSetup) Setup();
		if (Reliable == Unreliable) {
			Reliable.Host(port);
		}
		else {
			Reliable.Host(port);
			Unreliable.Host(port);
		}
	}

	public void SendHeaderReliable(object header, ushort id)
	{
		Reliable.RelayHeader(header, id);
	}

	public void SendHeaderReliable(object header, params ushort[] ids)
	{
		Reliable.RelayHeader(header, ids);
	}
	
	public void SendHeaderReliableExcept(object header, ushort id) {
		Reliable.RelayHeaderExcept(header, id);
	}

	public void SendHeaderReliableExcept(object header, params ushort[] ids) {
		Reliable.RelayHeaderExcept(header, ids);
	}

	public void SendHeaderReliable(object header)
	{
		Reliable.RelayHeaderAll(header);
	}

	public void SendHeaderUnreliable(object header, ushort id)
	{
		Unreliable.RelayHeader(header, id);
	}

	public void SendHeaderUnreliable(object header, params ushort[] ids)
	{
		Unreliable.RelayHeader(header, ids);
	}

	public void SendHeaderUnreliableExcept(object header, ushort id) {
		Unreliable.RelayHeaderExcept(header, id);
	}

	public void SendHeaderUnreliableExcept(object header, params ushort[] ids) {
		Unreliable.RelayHeaderExcept(header, ids);
	}

	public void SendHeaderUnreliable(object header)
	{
		Unreliable.RelayHeaderAll(header);
	}

	public void SendReliable<T>(T data, ushort id) where T : IPacket
	{
		Reliable.Relay(data, id);
	}

	public void SendReliable<T>(T data, params ushort[] ids) where T : IPacket
	{
		Reliable.Relay(data, ids);
	}

	public void SendReliableExcept<T>(T data, ushort id) where T : IPacket {
		Reliable.RelayExcept(data, id);
	}

	public void SendReliableExcept<T>(T data, params ushort[] ids) where T : IPacket {
		Reliable.RelayExcept(data, ids);
	}

	public void SendReliable<T>(T data) where T : IPacket
	{
		Reliable.RelayAll(data);
	}

	public void SendUnreliable<T>(T data, ushort id) where T : IPacket
	{
		Unreliable.Relay(data, id);
	}

	public void SendUnreliable<T>(T data, params ushort[] ids) where T : IPacket
	{
		Unreliable.Relay(data, ids);
	}

	public void SendUnreliableExcept<T>(T data, ushort id) where T : IPacket {
		Unreliable.RelayExcept(data, id);
	}

	public void SendUnreliableExcept<T>(T data, params ushort[] ids) where T : IPacket {
		Unreliable.RelayExcept(data, ids);
	}

	public void SendUnreliable<T>(T data) where T : IPacket
	{
		Unreliable.RelayAll(data);
	}

	public void SpawnReceive(CancellationToken ct)
	{
		Task.Run(async () => await Reliable.ProcessConnections(ct));
		if (Reliable != Unreliable)
			Task.Run(async () => await Unreliable.ProcessConnections(ct));
	}
}