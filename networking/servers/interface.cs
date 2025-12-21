using System;
using System.Threading;
using tairasoul.unity.common.bits;
using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.servers;

partial interface IServer {
	public void OnConnect(Action<object, ushort> action);
	public void Close();
	public void Flush();
	public void RelayAll<T>(T packet) where T : IPacket;
	public void Relay<T>(T packet, ushort player) where T : IPacket;
	public void Relay<T>(T packet, ushort[] players) where T : IPacket;
	public void RelayExcept<T>(T packet, ushort player) where T : IPacket;
	//public void RegisterPacketProcessor<T>(PacketType type, Action<T, ushort> processor) where T : IPacket;
	public void ProcessConnections(CancellationToken ct);
}