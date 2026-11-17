using System;

namespace tairasoul.unity.common.networking.attributes.handling;

/// <summary>
/// types marked with this attribute will be scanned for packet handling methods
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PacketHandlerAttribute : Attribute;

/// <summary>
/// methods marked with this attribute handle a specific packet, used for no-data packets
/// </summary>
/// <param name="packet">the packet to handle</param>
[AttributeUsage(AttributeTargets.Method)]
public class ExplicitHandlerAttribute(object packet) : Attribute;

/// <summary>
/// interfaces marked with this attribute can be used to blanket-handle all packets that derive from it
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public class BlanketInterfaceAttribute : Attribute;

/// <summary>
/// methods marked with this attribute will act as a "specialization", making the packet skip routing to its blanket interface
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class SpecializeBlanketAttribute : Attribute;