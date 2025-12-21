using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.factories;

partial class TcpUdpHybridFactory : ITransportFactory
{
	public IClient CreateReliableClient(string host, int port)
	{
		return new ClientTcp(host, port);
	}

	public IClient CreateUnreliableClient(IClient reliable, string host, int hostPort, int localPort)
	{
		return new ClientUdp(host, hostPort, localPort);
	}

	public IServer CreateReliableServer(int port)
	{
		return new ServerTcp(port);
	}
}