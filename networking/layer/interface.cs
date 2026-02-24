using System;
using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.layer;

public enum PacketReliability
{
	Reliable,
	Unreliable
}

public partial interface INetworkLayer {
	public bool IsHost();
	public ulong IncrementAndGetLocalCounter();
	public ushort GetPlayerID();
	public void ConnectTo(string host, int port);
	public void OnPacket<T>(object type, Action<T, ushort> listener) where T : IPacket;
	public (string username, ushort id)[] GetPlayers();
	public void SendPacketHeader(object header, ushort id);
	public void SendPacketHeader(object header, params ushort[] ids);
	public void SendPacketHeader(object header);
	public void SendPacketHeader(object header, PacketReliability reliability, ushort id);
	public void SendPacketHeader(object header, PacketReliability reliability, params ushort[] ids);
	public void SendPacketHeader(object header, PacketReliability reliability);
	public void SendPacket<T>(T data, ushort id) where T : IPacket;
	public void SendPacket<T>(T data, params ushort[] ids) where T : IPacket;
	public void SendPacket<T>(T data) where T : IPacket;
	public void SendPacket<T>(T data, PacketReliability reliability, ushort id) where T : IPacket;
	public void SendPacket<T>(T data, PacketReliability reliability, params ushort[] ids) where T : IPacket;
	public void SendPacket<T>(T data, PacketReliability reliability) where T : IPacket;
	public void Flush();
	public void Synchronize();
	public void ReceiveLoop();
	public void Disconnect();
}