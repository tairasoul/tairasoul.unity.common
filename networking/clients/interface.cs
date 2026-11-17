using System;
using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.layer;

namespace tairasoul.unity.common.networking.clients;

/// <summary>
/// client interface for layers
/// </summary>
public partial interface IClient {
	public void Connect(string host, int port);
	public void Disconnect();
	public void Flush();
	public void SendPacketHeader(object header);
	public void SendPacket<T>(T packet) where T : IPacket;
	public Task ProcessPackets(CancellationToken cts);
}