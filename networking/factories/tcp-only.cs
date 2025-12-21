using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.factories;

class TcpOnlyFactory : ITransportFactory
{
	public IClient CreateReliableClient(string host, int port)
	{
		return new ClientTcp(host, port);
	}

	public IClient CreateUnreliableClient(IClient reliable, string host, int hostPort, int localPort)
	{
		return reliable;
	}

	public IServer CreateReliableServer(int port)
	{
		return new ServerTcp(port);
	}

	public IServer CreateUnreliableServer(IServer reliable, int port)
	{
		return reliable;
	}
}