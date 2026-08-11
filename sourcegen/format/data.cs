using System.Collections.Immutable;

namespace tairasoul.unity.common.sourcegen.format;

enum Primitive {
	String,
	Double,
	Float,
	Int,
	UInt,
	Short,
	UShort,
	Long,
	ULong,
	Bool,
	Byte,
	SByte
}

abstract record FormatItem();
record FormatPrimitive(Primitive primitive) : FormatItem;
record FormatPacked(ImmutableArray<FormatItem> pack, ImmutableArray<Primitive> primitives) : FormatItem;
record FormatEnum(string qualifiedName, ImmutableArray<string> values) : FormatItem;
record FormatStructField(string name, FormatItem item, bool @private, bool positional, bool nullable) : FormatItem;
record FormatStruct(string qualifiedName, string @namespace, ImmutableArray<FormatStructField> fields, bool @record, bool @public, bool isUnmanaged, bool isClass) : FormatItem;
record FormatArray(FormatItem element, bool elementNullable) : FormatItem;
record FormatDictionary(FormatItem key, FormatItem value, bool valueNullable, string qualifiedName) : FormatItem;
record FormatQualifiedReference(string qualifiedName, string @namespace) : FormatItem;
record FormatInterface(ImmutableArray<FormatQualifiedReference> derives, string qualifiedName) : FormatItem;