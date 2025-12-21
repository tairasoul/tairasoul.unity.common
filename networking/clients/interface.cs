using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.clients;

partial interface IClient {
	public void Disconnect();
	public void Flush();
	public void SendPacket<T>(T packet) where T : IPacket;
	//public void RegisterPacketProcessor<T>(PacketType type, Action<T> processor) where T : IPacket;
	public Task ProcessPackets(CancellationToken cts);
}