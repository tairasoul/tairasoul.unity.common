using System;
using System.Threading;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.servers;

public partial interface IServer {
	public void OnConnect(Action<object, ushort> action);
	public void Close();
	public void Flush();
	public void RelayHeader(object header, ushort id);
	public void RelayHeader(object header, params ushort[] players);
	public void RelayHeaderAll(object header);
	public void RelayHeaderExcept(object header, params ushort[] except);
	public void RelayAll<T>(T packet) where T : IPacket;
	public void Relay<T>(T packet, ushort player) where T : IPacket;
	public void Relay<T>(T packet, params ushort[] players) where T : IPacket;
	public void RelayExcept<T>(T packet, params ushort[] except) where T : IPacket;
	public void RegisterPacketProcessor<T>(object type, Action<T, ushort> processor) where T : IPacket;
	public void ProcessConnections(CancellationToken ct);
}