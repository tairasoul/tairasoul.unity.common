using System;

namespace tairasoul.unity.common.attributes.bits;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ItemBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Enum)]
public class EnumAutoBitsize : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class LengthBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ArrayItemBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class DictionaryKeyBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class DictionaryValueBitSize(ushort size) : Attribute { }