using tairasoul.unity.common.networking.interfaces;

namespace tairasoul.unity.common.networking.layer;

enum PacketReliability
{
	Reliable,
	Unreliable
}

partial interface INetworkLayer {
	public bool IsHost();
	public ulong IncrementAndGetLocalCounter();
	public ushort GetPlayerID();
	public void ConnectTo(string host, int port);
	// public abstract void OnPacket<T>(PacketType type, Action<T, ushort> listener) where T : IPacket;
	public (string username, ushort id)[] GetPlayers();
	public void SendPacket<T>(T data, ushort id) where T : IPacket;
	public void SendPacket<T>(T data, ushort[] ids) where T : IPacket;
	public void SendPacket<T>(T data) where T : IPacket;
	public void SendPacket<T>(T data, ushort id, PacketReliability reliability) where T : IPacket;
	public void SendPacket<T>(T data, ushort[] ids, PacketReliability reliability) where T : IPacket;
	public void SendPacket<T>(T data, PacketReliability reliability) where T : IPacket;
	public void Flush();
	public void Synchronize();
	public void ReceiveLoop();
	public void Disconnect();
}