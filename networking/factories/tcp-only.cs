using System.Net;
using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.layer;
using tairasoul.unity.common.networking.protocols.tcp;
using tairasoul.unity.common.networking.servers;
using TcpConnection = tairasoul.unity.common.networking.protocols.tcp.TcpConnection;

namespace tairasoul.unity.common.networking.factories;

public class TcpOnlyFactory : IConnectionFactory
{
	class TcpOnlyCoordinator(IServerLayer layer) : IServerCoordinator
	{
		readonly IServerLayer layer = layer;

		public ushort StartingID => layer.StartingID;

		public PlayerTracker Tracker => layer.Tracker;

		public void ClientConnected(IServer origin, ushort id, IConnection connection, IPEndPoint endPoint)
		{
			ConnectionPair pair = new(connection)
			{
				Unreliable = connection
			};
			Tracker.Track(id, pair);
			layer.PlayerConnect(origin, id);
		}

		public IConnection ConnectionFromPair(IServer thisServer, ConnectionPair pair)
		{
			return layer.ConnectionFromPair(thisServer, pair);
		}

		public void HandleDisconnect(IServer origin, ushort id) {
			layer.PlayerDisconnect(origin, id);
		}
	}

	public IServer ReliableServer()
	{
		return new ServerTcp();
	}

	public IServer UnreliableServer(IServer reliable)
	{
		return reliable;
	}

	public IServerCoordinator CreateCoordinator(IServerLayer layer)
	{
		return new TcpOnlyCoordinator(layer);
	}

	public IClient ReliableClient()
	{
		return new ClientTcp();
	}

	public IClient UnreliableClient(IClient reliable)
	{
		return reliable;
	}
}