using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.clients;
using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.layer;

public class ClientLayer : IClientLayer
{
	public IConnectionFactory ConnectionFactory { get; set; }
	public IClient Reliable { get; set; }
	public IClient Unreliable { get; set; }
	bool NeedsSetup = true;

	public void Connect(string host, int port)
	{
		if (NeedsSetup) Setup();
		if (Reliable == Unreliable) {
			Reliable.Connect(host, port);
		}
		else {
			Reliable.Connect(host, port);
			Unreliable.Connect(host, port);
		}
	}

	public void Disconnect()
	{
		if (Reliable == Unreliable) {
			Reliable.Disconnect();
		}
		else {
			Reliable.Disconnect();
			Unreliable.Disconnect();
		}
	}

	public void Flush()
	{
		if (Reliable == Unreliable) {
			Reliable.Flush();
		}
		else {
			Reliable.Flush();
			Unreliable.Flush();
		}
	}

	public void SendHeaderReliable(object header)
	{
		Reliable.SendPacketHeader(header);
	}

	public void SendHeaderUnreliable(object header)
	{
		Unreliable.SendPacketHeader(header);
	}

	public void SendReliable<T>(T data) where T : IPacket
	{
		Reliable.SendPacket(data);
	}

	public void SendUnreliable<T>(T data) where T : IPacket
	{
		Unreliable.SendPacket(data);
	}

	public void Setup()
	{
		if (!NeedsSetup) return;
		Reliable = ConnectionFactory.ReliableClient();
		Unreliable = ConnectionFactory.UnreliableClient(Reliable);
	}

	public void Cleanup() {
		Disconnect();
		Reliable = null;
		Unreliable = null;
		NeedsSetup = true;
	}

	public void SpawnReceive(CancellationToken ct)
	{
		Task.Run(async () => await Reliable.ProcessPackets(ct), ct);
		if (Reliable != Unreliable)
			Task.Run(async () => await Unreliable.ProcessPackets(ct), ct);
	}
}