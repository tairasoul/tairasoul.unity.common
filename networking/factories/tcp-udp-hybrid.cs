using System.Net;
using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.layer;
using tairasoul.unity.common.networking.protocols.tcp;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.factories;

public class TcpUdpHybridFactory : IConnectionFactory
{
	class TcpUdpHybridCoordinator(IServerLayer layer) : IServerCoordinator {
		readonly IServerLayer layer = layer;
		public ushort StartingID => layer.StartingID;
		public PlayerTracker Tracker => layer.Tracker;

		public void ClientConnected(IServer origin, ushort id, IConnection connection, IPEndPoint endPoint)
		{
			if (origin == layer.Reliable) {
				ConnectionPair pair = new(connection);
				Tracker.Track(id, pair);
				var serv = (ServerUdp)layer.Unreliable;
				serv.TcpConn(endPoint, id);
			}
			else {
				var pair = Tracker.GetPair(id);
				if (pair != null) {
					pair.Unreliable = connection;
				}
			}
			layer.PlayerConnect(origin, id);
		}

		public IConnection ConnectionFromPair(IServer thisServer, ConnectionPair pair)
		{
			return layer.ConnectionFromPair(thisServer, pair);
		}

		public void HandleDisconnect(IServer origin, ushort id) {
			var pair = Tracker.GetPair(id);
			if (pair != null) {
				pair.Unreliable!.Connected = false;
			}
			layer.PlayerDisconnect(origin, id);
		}
	}

	public IServerCoordinator CreateCoordinator(IServerLayer layer)
	{
		return new TcpUdpHybridCoordinator(layer);
	}

	public IServer ReliableServer()
	{
		return new ServerTcp();
	}

	public IServer UnreliableServer(IServer reliable)
	{
		return new ServerUdp();
	}

	public IClient ReliableClient()
	{
		return new ClientTcp();
	}

	public IClient UnreliableClient(IClient reliable)
	{
		return new ClientUdp();
	}
}