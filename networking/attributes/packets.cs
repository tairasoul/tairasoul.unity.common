using System;

namespace tairasoul.unity.common.networking.attributes.packets;

enum InternalPacketTypes {
	PacketBatchEnd,
	IdRelay,
	Connect,
	Disconnect,
	PlayerConnected
}

enum ServerRelayType {
	RelayAll,
	RelayExceptSender
}

enum PacketReliability {
	Reliable,
	Unreliable
}

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
class CorrelatesTo(object @enum) : Attribute {}

[AttributeUsage(AttributeTargets.Field)]
class CorrelatesToInternal(InternalPacketTypes iType) : Attribute {}

[AttributeUsage(AttributeTargets.Field)]
class ServerRelay(ServerRelayType handling) : Attribute {}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
class Reliability(PacketReliability reliability) : Attribute {}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class VariantOf(Type[] types) : Attribute {}

[AttributeUsage(AttributeTargets.Enum)]
class PacketTypeIdentifier() : Attribute {}

[AttributeUsage(AttributeTargets.Class)]
class ImplementUnreliableRead() : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
class ImplementReliableRead() : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
class ImplementUnreliableHeaderWrite() : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
class ImplementReliableHeaderWrite() : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
class ImplementReliabilityGet() : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
class ImplementServerRelay() : Attribute { }