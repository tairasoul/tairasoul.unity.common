using System;

namespace tairasoul.unity.common.networking.attributes.packets;

/// <summary>
/// what type this packet gets sent with
/// </summary>
/// <param name="data">packet data type</param>
[AttributeUsage(AttributeTargets.Field)]
public class CorrelatesTo(Type data) : Attribute {
	public Type correlation = data;
}

/// <summary>
/// this packet type has an empty body
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class EmptyBody : Attribute;

/// <summary>
/// enums marked with this attribute will be scanned for packet types
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public class PacketTypeIdentifier : Attribute;