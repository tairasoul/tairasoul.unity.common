using System;

namespace tairasoul.unity.common.networking.attributes.packets;

enum InternalPacketTypes {
	PacketBatchEnd,
	IdRelay,
	Connect,
	Disconnect,
	PlayerConnected
}

public enum ServerRelayType {
	RelayAll,
	RelayExceptSender
}

public enum PacketReliability {
	Reliable,
	Unreliable
}

[AttributeUsage(AttributeTargets.Field)]
public class CorrelatesTo(Type data) : Attribute {
	public Type correlation = data;
}

[AttributeUsage(AttributeTargets.Field)]
class CorrelatesToInternal(InternalPacketTypes iType) : Attribute {
	public InternalPacketTypes packet = iType;
}

[AttributeUsage(AttributeTargets.Field)]
public class ServerRelay(ServerRelayType handling) : Attribute {
	public ServerRelayType handling = handling;
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class Reliability(PacketReliability reliability) : Attribute {
	public PacketReliability reliability = reliability;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class VariantOf(Type[] types) : Attribute {}

[AttributeUsage(AttributeTargets.Enum)]
public class PacketTypeIdentifier() : Attribute {}