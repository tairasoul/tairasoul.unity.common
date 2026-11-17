namespace tairasoul.unity.common.networking.interfaces;

public interface IRoutedPacket {
	public void Route(bool isHost, ushort playerId);
}