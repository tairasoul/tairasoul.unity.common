using System;

namespace tairasoul.unity.common.attributes.bits;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class ItemBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Enum)]
class EnumAutoBitsize : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class LengthBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class ArrayItemBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class DictionaryKeyBitSize(ushort size) : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
class DictionaryValueBitSize(ushort size) : Attribute { }