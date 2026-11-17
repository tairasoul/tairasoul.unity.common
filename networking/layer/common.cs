using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using tairasoul.unity.common.format;
using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.layer;

/// <summary>
/// generic connection (udp, tcp, anything that can send/receive data and has some form of endpoint)
/// </summary>
public interface IConnection {
	/// <summary>
	/// connect to a destination
	/// </summary>
	/// <param name="host">host to connect to</param>
	/// <param name="port">port to connect to</param>
	public void Connect(string host, int port);
	/// <summary>
	/// create from an existing read/write pair
	/// </summary>
	/// <param name="read">read stream</param>
	/// <param name="write">write stream</param>
	public void Create(Stream read, Stream write);
	/// <summary>
	/// whether or not this connection has been opened yet
	/// </summary>
	public bool Connected { get; set; }
	/// <summary>
	/// reader for this connection
	/// </summary>
	public FormatReader Reader { get; set; }
	/// <summary>
	/// writer for this connection
	/// </summary>
	public FormatWriter Writer { get; set; }
	/// <summary>
	/// Flush() will not do anything if this is set to false (and it will set this back to false after calling)
	/// </summary>
	public bool NeedsFlush { get; set; }
	/// <summary>
	/// close this connection
	/// </summary>
	public void Close();
	/// <summary>
	/// flush written data
	/// </summary>
	public void Flush();
	/// <summary>
	/// recheck connection state (ex. for tcp)
	/// </summary>
	public void RecheckConnected();
}

/// <summary>
/// generic connection factory
/// </summary>
public interface IConnectionFactory {
	/// <summary>
	/// create a reliable client
	/// </summary>
	/// <returns>reliable client instance</returns>
	public IClient ReliableClient();
	/// <summary>
	/// create an unreliable client
	/// </summary>
	/// <param name="reliable">previously created reliable client</param>
	/// <returns>unreliable client instance</returns>
	public IClient UnreliableClient(IClient reliable);
	/// <summary>
	/// create a reliable server
	/// </summary>
	/// <returns>reliable server instance</returns>
	public IServer ReliableServer();
	/// <summary>
	/// create an unreliable server
	/// </summary>
	/// <param name="reliable">previously created reliable server</param>
	/// <returns>unreliable server instance</returns>
	public IServer UnreliableServer(IServer reliable);
	/// <summary>
	/// create the coordinator for this factory
	/// </summary>
	/// <param name="layer">the layer for the coordinator</param>
	/// <returns>a coordinator instance</returns>
	public IServerCoordinator CreateCoordinator(IServerLayer layer);
}

/// <summary>
/// reliable-unreliable pair of connections
/// </summary>
public class ConnectionPair(IConnection reliable)
{
	/// <summary>
	/// reliable connection instance
	/// </summary>
	public IConnection Reliable = reliable;
	/// <summary>
	/// unreliable connection instance (will usually initially be null)
	/// </summary>
	public IConnection? Unreliable;

	/// <summary>
	/// flush both connections in this pair (or only the reliable one)
	/// </summary>
	public void Flush() {
		Reliable.Flush();
		if (Reliable != Unreliable)
			Unreliable?.Flush();
	}
}

/// <summary>
/// class responsible for tracking id -> connection associations
/// </summary>
public class PlayerTracker {
	readonly ConcurrentDictionary<ushort, ConnectionPair> tracked = [];
	// readonly ConcurrentDictionary<ConnectionPair, ushort> reverse = [];

	/// <summary>
	/// track a specific connection pair by an id
	/// </summary>
	/// <param name="id">id to track by</param>
	/// <param name="pair">connection pair associated</param>
	public void Track(ushort id, ConnectionPair pair) {
		tracked.TryAdd(id, pair);
		// reverse.TryAdd(pair, id);
	}

	/// <summary>
	/// try to get the pair associated with a id
	/// </summary>
	/// <param name="id">id to get</param>
	/// <returns>associated pair or null if no pair is associated with this id</returns>
	public ConnectionPair? GetPair(ushort id) {
		tracked.TryGetValue(id, out ConnectionPair? pair);
		return pair;
	}

	/// <summary>
	/// check if a pair exists by id
	/// </summary>
	/// <param name="id">pair id to check</param>
	/// <returns>if a pair exists with that id</returns>
	public bool HasPair(ushort id) {
		return tracked.ContainsKey(id);
	}

	/// <summary>
	/// untrack all pairs
	/// </summary>
	public void UntrackAll() {
		foreach (var key in tracked.Keys.ToArray()) {
			Untrack(key);
		}
	}

	/// <summary>
	/// enumerate over all pairs
	/// </summary>
	/// <returns>enumerable for all pairs</returns>
	public IEnumerable<(ushort id, ConnectionPair pair)> GetPairs() {
		foreach (var kv in tracked) {
			yield return (kv.Key, kv.Value);
		}
	}

	/// <summary>
	/// untrack a specific pair
	/// </summary>
	/// <param name="id">id to untrack</param>
	public void Untrack(ushort id) {
		var pair = GetPair(id);
		if (pair == null) return;
		pair.Reliable?.Close();
		pair.Unreliable?.Close();
		tracked.TryRemove(id, out _);
	}
}

/// <summary>
/// server networking layer
/// </summary>
public interface IServerLayer : ILayer {
	public PlayerTracker Tracker { get; set; }
	public ushort StartingID { get; set; }
	public IConnectionFactory ConnectionFactory { get; set; }
	public IServer Reliable { get; set; }
	public IServer Unreliable { get; set; }
	public event Action<IServer, ushort> HandleDisconnect;
	public event Action<IServer, ushort> HandleConnect;
	public void PlayerDisconnect(IServer server, ushort id);
	public void PlayerConnect(IServer server, ushort id);
	public IConnection ConnectionFromPair(IServer server, ConnectionPair pair);
	public void SendReliable<T>(T data, ushort id) where T : IPacket;
	public void SendReliable<T>(T data, params ushort[] ids) where T : IPacket;
	public void SendReliableExcept<T>(T data, ushort id) where T : IPacket;
	public void SendReliableExcept<T>(T data, params ushort[] ids) where T : IPacket;
	public void SendUnreliable<T>(T data, ushort id) where T : IPacket;
	public void SendUnreliable<T>(T data, params ushort[] ids) where T : IPacket;
	public void SendUnreliableExcept<T>(T data, ushort id) where T : IPacket;
	public void SendUnreliableExcept<T>(T data, params ushort[] ids) where T : IPacket;
	public void SendHeaderReliable(object header, ushort id);
	public void SendHeaderReliable(object header, params ushort[] ids);
	public void SendHeaderReliableExcept(object header, ushort id);
	public void SendHeaderReliableExcept(object header, params ushort[] ids);
	public void SendHeaderUnreliable(object header, ushort id);
	public void SendHeaderUnreliable(object header, params ushort[] ids);
	public void SendHeaderUnreliableExcept(object header, ushort id);
	public void SendHeaderUnreliableExcept(object header, params ushort[] ids);
	public void Host(int port);
	public void Close();
	public void SpawnReceive(CancellationToken ct);
}

/// <summary>
/// client networking layer
/// </summary>
public interface IClientLayer : ILayer {
	public IConnectionFactory ConnectionFactory { get; set; }	
	public IClient Reliable { get; set; }
	public IClient Unreliable { get; set; }
	public void Connect(string host, int port);
	public void Disconnect();
	public void SpawnReceive(CancellationToken ct);
}

/// <summary>
/// base type for all layers, includes common methods by the same name that function practically the same on both ends
/// </summary>
public interface ILayer {
	public void Setup();
	public void Cleanup();
	public void Flush();
	public void SendReliable<T>(T data) where T : IPacket;
	public void SendUnreliable<T>(T data) where T : IPacket;
	public void SendHeaderReliable(object header);
	public void SendHeaderUnreliable(object header);
}

/// <summary>
/// class containing the active networking instance
/// </summary>
public static class LayerInstance
{
	/// <summary>
	/// can be either an IClientLayer or an IServerLayer
	/// </summary>
	public static ILayer? Instance;
}

/// <summary>
/// coordinates the reliable/unreliable server and the layer they are within (ie. making tcp update udp's playerlist or referring to the layer for player tracker)
/// </summary>
public interface IServerCoordinator {
	public void ClientConnected(IServer origin, ushort id, IConnection connection, IPEndPoint endPoint);
	public void HandleDisconnect(IServer origin, ushort id);
	public IConnection ConnectionFromPair(IServer thisServer, ConnectionPair pair);
	public PlayerTracker Tracker { get; }
	public ushort StartingID { get; }
}