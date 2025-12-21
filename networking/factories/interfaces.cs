using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.servers;

namespace tairasoul.unity.common.networking.factories;

interface ITransportFactory {
	public IClient CreateReliableClient(string host, int port);
	public IClient CreateUnreliableClient(IClient reliable, string host, int hostPort, int localPort);
	public IServer CreateReliableServer(int port);
	public IServer CreateUnreliableServer(IServer reliable, int port);
}