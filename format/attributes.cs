using System;

namespace tairasoul.unity.common.format.attributes;

[AttributeUsage(AttributeTargets.Class)]
public class FormatMethodsForAttribute(Type target) : Attribute {
	public Type target = target;
}

/// <summary>
/// give this type a custom ser/de implementation
/// </summary>
/// <param name="implementingType">type implementing format methods</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class CustomSerde(Type implementingType) : Attribute {
	public Type implementingType = implementingType;
}

/// <summary>
/// gives a type not in this assembly a custom ser/de implementation
/// </summary>
/// <param name="implementingType">type implementing format methods</param>
/// <param name="targetType">type this formatter is for</param>
[AttributeUsage(AttributeTargets.Assembly)]
public class CustomSerdeFor(Type implementingType, Type targetType) : Attribute {
	public Type implementingType = implementingType;
	public Type targetType = targetType;
}

/// <summary>
/// whether or not this format type has an associated shadow struct
/// </summary>
/// <param name="shadowType">shadow struct type containing all unmanaged fields within the struct</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class HasShadowStruct(Type shadowType) : Attribute {
	public Type shadowType = shadowType;
}

/// <summary>
/// this enum will be represented by the index of the enum rather than the value
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class CompactedEnumRepresentation : Attribute;

// unused, implementation will be attempted later
// [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
// public class ShouldPackAttribute : Attribute;