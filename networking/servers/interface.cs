using System;
using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.interfaces;
using tairasoul.unity.common.networking.layer;

namespace tairasoul.unity.common.networking.servers;

/// <summary>
/// server interface for layers
/// </summary>
public interface IServer {
	public IServerCoordinator Coordinator { get; set; }
	public void Close();
	public void RelayHeader(object header, ushort id);
	public void RelayHeader(object header, params ushort[] players);
	public void RelayHeaderAll(object header);
	public void RelayHeaderExcept(object header, params ushort[] except);
	public void RelayAll<T>(T packet) where T : IPacket;
	public void Relay<T>(T packet, ushort player) where T : IPacket;
	public void Relay<T>(T packet, params ushort[] players) where T : IPacket;
	public void RelayExcept<T>(T packet, params ushort[] except) where T : IPacket;
	public Task ProcessConnections(CancellationToken ct);
	public void Host(int port);
}