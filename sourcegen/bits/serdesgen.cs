using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static tairasoul.unity.common.sourcegen.bits.util.StringUtil;

namespace tairasoul.unity.common.sourcegen.bits;

enum PrimitiveType {
	String,
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

abstract record SerdesType();
record SerdesTypeVariant(ImmutableArray<SerdesType> variants) : SerdesType;
record SerdesTypeStructField(string name, SerdesType type, bool useReflection = false, uint? size = null, bool isPositional = false, bool isNullable = false) : SerdesType;
record SerdesTypeStruct(string qualifiedName, ImmutableArray<SerdesTypeStructField> fields, bool isRecord) : SerdesType;
record SerdesTypeArray(SerdesType elementType, uint? lengthSize = null, uint? valueSize = null, bool elementNullable = false) : SerdesType;
record SerdesTypeDictionary(SerdesType key, SerdesType value, uint? lengthSize = null, uint? keySize = null, uint? valueSize = null, bool valueNullable = false) : SerdesType;
record SerdesTypePrimitive(PrimitiveType primitive) : SerdesType;
record SerdesTypeEnum(PrimitiveType primitive, string qualifiedName, ImmutableArray<string> values, bool autoSize = false) : SerdesType;
record SerdesTypeQualifiedReference(string qualifiedName) : SerdesType;

class SerdesGen {
	const string GeneratedCodeData = "\"tairasoul.unity.common.sourcegen.bits\", \"0.1.0\"";

	public static bool Predicate(SyntaxNode node) {
		if (node is InvocationExpressionSyntax invoc) {
			if (invoc.Expression is MemberAccessExpressionSyntax ma) {
				if (ma.Name is IdentifierNameSyntax { Identifier.Text: "Read" or "Write" } || ma.Name is GenericNameSyntax { Identifier.Text: "Read" or "Write" })
				{
					return true;
				}
			}
		}
		return false;
	}

	static ushort? GetUshortAttr(ISymbol symbol, string attrN) {
		AttributeData? attr = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == attrN);
		if (attr == null) return null;
		if (attr.ConstructorArguments.FirstOrDefault().Value is ushort size)
			return size;
		return null;
	}

	static ushort? GetItemBitSize(ISymbol symbol) {
		return GetUshortAttr(symbol, "ItemBitSize");
	}

	static ushort? GetLengthSize(ISymbol symbol) {
		return GetUshortAttr(symbol, "LengthBitSize");
	}

	static ushort? GetArrayItemSize(ISymbol symbol) {
		return GetUshortAttr(symbol, "ArrayItemBitSize");
	}

	static ushort? GetDictionaryKeySize(ISymbol symbol) {
		return GetUshortAttr(symbol, "DictionaryKeyBitSize");
	}

	static ushort? GetDictionaryValueSize(ISymbol symbol) {
		return GetUshortAttr(symbol, "DictionaryValueBitSize");
	}

	public static SerdesType?[] Transform(SyntaxNode syntax, SemanticModel semantic, CancellationToken ct) {
		var ma = (InvocationExpressionSyntax)syntax;
		var methodSymbol = semantic.GetSymbolInfo(ma.Expression).Symbol as IMethodSymbol;
		var targetSymbol = methodSymbol?.ReceiverType;

		string s = targetSymbol?.ToDisplayString(format);

		if (s != "tairasoul.unity.common.bits.BitReader" && s != "tairasoul.unity.common.bits.BitWriter" && s != "tairasoul.unity.common.bits.BitWriterAsync" && s != "tairasoul.unity.common.bits.BitReaderAsync")
      return [];
    var argExpression = ma.ArgumentList.Arguments[0].Expression;
    var argTypeInfo = semantic.GetTypeInfo(argExpression);
    ISymbol? symbol = argTypeInfo.Type;
		if (symbol is null) return [];
		HashSet<string> encountered = [];
		List<SerdesType?> extras = [];
		if (symbol is INamedTypeSymbol named) {
			if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
				return [ProcessStruct(named, encountered, extras), ..extras];
			}
			else if (named.TypeKind == TypeKind.Enum) {
				return [ProcessEnum(named)];
			}
		}
		return [];
	}

	static readonly SymbolDisplayFormat format = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
	);

	static ImmutableArray<SerdesType> GetVariants(ISymbol symbol, HashSet<string> encountered, List<SerdesType?> extras) {
		List<SerdesType> variants = [];
		foreach (AttributeData attr in symbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString(format) == "tairasoul.unity.common.networking.attributes.packets.VariantOf") {
				if (attr.ConstructorArguments.First().Values is ImmutableArray<TypedConstant> attrArr)
				{
					foreach (ISymbol sym in attrArr.Where(v => v.Value is ISymbol).Select(v => v.Value).Cast<ISymbol>()) {
						if (sym is INamedTypeSymbol named)
						{
							if (named.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>")
							{
								ushort? lengthBits = GetLengthSize(named);
								ushort? keyBits = GetDictionaryKeySize(named);
								ushort? valueBits = GetDictionaryValueSize(named);
								SerdesType? s = ProcessDictionary(named, encountered, extras, lengthBits, keyBits, valueBits);
								if (s == null) continue;
								variants.Add(s);
							}
							else if (named.SpecialType == SpecialType.None && (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class))
							{
								string display = named.ToDisplayString(format);
								if (encountered.Add(display))
								{
									SerdesType s2 = ProcessStruct(named, encountered, extras);
									extras.Add(s2);
								}
								SerdesType s = new SerdesTypeQualifiedReference(display);
								variants.Add(s);
							}
							else if (named.TypeKind == TypeKind.Enum)
							{
								SerdesType? s = ProcessEnum(named);
								if (s == null) continue;
								variants.Add(s);
							}
							else if (IsPrimitive(named))
							{
								ushort? item = GetItemBitSize(named);
								SerdesType prim = new SerdesTypePrimitive(SpecialToPrimitive(named.SpecialType)!.Value);
								SerdesTypeStructField sfield = new(symbol.Name, prim, symbol.DeclaredAccessibility != Accessibility.Public, item, false);
								variants.Add(sfield);
							}
						}
						else if (sym is IArrayTypeSymbol arrSym) {
							ushort? length = GetLengthSize(sym);
							ushort? item = GetArrayItemSize(sym);
							SerdesType? arr = ProcessArray(arrSym, encountered, extras, length, item);
							if (arr == null) continue;
							variants.Add(arr);
						}
					}
				}
			}
		}
		return variants.ToImmutableArray();
	}

	static SerdesType ProcessStruct(INamedTypeSymbol symbol, HashSet<string> encountered, List<SerdesType?> extras) {
		List<SerdesTypeStructField> fields = [];
		foreach (IFieldSymbol field in symbol.GetMembers().OfType<IFieldSymbol>().Where(p => !p.IsImplicitlyDeclared)) {
			if (field.IsConst || field.IsReadOnly) continue;
			if (field.Type is INamedTypeSymbol named && named.SpecialType == SpecialType.None) {
				if (named.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>") {
					ushort? lengthBits = GetLengthSize(field);
					ushort? keyBits = GetDictionaryKeySize(field);
					ushort? valueBits = GetDictionaryValueSize(field);
					SerdesType? s = ProcessDictionary(named, encountered, extras, lengthBits, keyBits, valueBits);
					if (s == null) continue;
					SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, false, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
				else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
					string display = named.ToDisplayString(format);
					if (encountered.Add(display)) {
						SerdesType s2 = ProcessStruct(named, encountered, extras);
						extras.Add(s2);
					}
					SerdesType s = new SerdesTypeQualifiedReference(display);
					SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, false, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
				else if (named.TypeKind == TypeKind.Enum) {
					SerdesType? s = ProcessEnum(named);
					if (s == null) continue;
					SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, false, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
			}
			else if (field.Type is IArrayTypeSymbol arrSym) {
				ushort? length = GetLengthSize(field);
				ushort? item = GetArrayItemSize(field);
				SerdesType? arr = ProcessArray(arrSym, encountered, extras, length, item);
				if (arr == null) continue;
				SerdesTypeStructField sfield = new(field.Name, arr, field.DeclaredAccessibility != Accessibility.Public, null, false, field.NullableAnnotation == NullableAnnotation.Annotated);
				fields.Add(sfield);
			}
			else if (IsPrimitive(field.Type)) {
				ushort? item = GetItemBitSize(field);
				SerdesType prim = new SerdesTypePrimitive(SpecialToPrimitive(field.Type.SpecialType)!.Value);
				SerdesTypeStructField sfield = new(field.Name, prim, field.DeclaredAccessibility != Accessibility.Public, item, false, field.NullableAnnotation == NullableAnnotation.Annotated);
				fields.Add(sfield);
			}
			else if (field.Type.SpecialType == SpecialType.System_Object) {
				var variants = GetVariants(field, encountered, extras);
				if (variants.Length <= 0) continue;
				SerdesTypeStructField sfield = new(field.Name, new SerdesTypeVariant(variants), field.DeclaredAccessibility != Accessibility.Public, null, false, field.NullableAnnotation == NullableAnnotation.Annotated);
				fields.Add(sfield);
			}
		}
		if (symbol.IsRecord) {
			foreach (IPropertySymbol? field in symbol.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsImplicitlyDeclared)) {
				if (field.Type is INamedTypeSymbol named && field.Type.SpecialType == SpecialType.None) {
					if (named.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>") {
						ushort? lengthBits = GetLengthSize(field);
						ushort? keyBits = GetDictionaryKeySize(field);
						ushort? valueBits = GetDictionaryValueSize(field);
						SerdesType? s = ProcessDictionary(named, encountered, extras, lengthBits, keyBits, valueBits);
						if (s == null) continue;
						SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, true, field.NullableAnnotation == NullableAnnotation.Annotated);
						fields.Add(sfield);
					}
					else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
						string display = named.ToDisplayString(format);
						if (encountered.Add(display)) {
							SerdesType s2 = ProcessStruct(named, encountered, extras);
							extras.Add(s2);
						}
						SerdesType s = new SerdesTypeQualifiedReference(display);
						SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, true, field.NullableAnnotation == NullableAnnotation.Annotated);
						fields.Add(sfield);
					}
					else if (named.TypeKind == TypeKind.Enum) {
						SerdesType? s = ProcessEnum(named);
						if (s == null) continue;
						SerdesTypeStructField sfield = new(field.Name, s, field.DeclaredAccessibility != Accessibility.Public, null, true, field.NullableAnnotation == NullableAnnotation.Annotated);
						fields.Add(sfield);
					}
				}
				else if (field.Type is IArrayTypeSymbol arrSym) {
					ushort? length = GetLengthSize(field);
					ushort? item = GetArrayItemSize(field);
					SerdesType? arr = ProcessArray(arrSym, encountered, extras, length, item);
					if (arr == null) continue;
					SerdesTypeStructField sfield = new(field.Name, arr, field.DeclaredAccessibility != Accessibility.Public, null, true, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
				else if (IsPrimitive(field.Type)) {
					ushort? item = GetItemBitSize(field);
					SerdesType prim = new SerdesTypePrimitive(SpecialToPrimitive(field.Type.SpecialType)!.Value);
					SerdesTypeStructField sfield = new(field.Name, prim, field.DeclaredAccessibility != Accessibility.Public, item, true, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
				else if (field.Type.SpecialType == SpecialType.System_Object) {
					var variants = GetVariants(field, encountered, extras);
					if (variants.Length <= 0) continue;
					SerdesTypeStructField sfield = new(field.Name, new SerdesTypeVariant(variants), field.DeclaredAccessibility != Accessibility.Public, null, true, field.NullableAnnotation == NullableAnnotation.Annotated);
					fields.Add(sfield);
				}
			}
		}
		return new SerdesTypeStruct(symbol.ToDisplayString(format), fields.ToImmutableArray(), symbol.IsRecord);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static PrimitiveType? SpecialToPrimitive(SpecialType special) {
		return special switch
		{
			SpecialType.System_Byte => PrimitiveType.Byte,
			SpecialType.System_Single => PrimitiveType.Float,
			SpecialType.System_SByte => PrimitiveType.SByte,
			SpecialType.System_Int16 => PrimitiveType.Short,
			SpecialType.System_UInt16 => PrimitiveType.UShort,
			SpecialType.System_Int32 => PrimitiveType.Int,
			SpecialType.System_UInt32 => PrimitiveType.UInt,
			SpecialType.System_Int64 => PrimitiveType.Long,
			SpecialType.System_UInt64 => PrimitiveType.ULong,
			SpecialType.System_String => PrimitiveType.String,
			SpecialType.System_Boolean => PrimitiveType.Bool,
			_ => null
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPrimitive(SpecialType special) {
		return SpecialToPrimitive(special) != null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPrimitive(ITypeSymbol symbol) {
		return IsPrimitive(symbol.SpecialType);
	}

	static SerdesType? ProcessDictionary(INamedTypeSymbol symbol, HashSet<string> encountered, List<SerdesType?> extras, ushort? lengthBits = null, ushort? keyBits = null, ushort? valueBits = null) {
		if (symbol.TypeArguments.Length < 2 || 
    symbol.TypeArguments[0] is not { } keyType || 
    symbol.TypeArguments[1] is not { } valueType) {
			throw new Exception("this shouldn't happen");
		}
		SerdesType key;
		SerdesType value;
		if (IsPrimitive(keyType)) {
			key = new SerdesTypePrimitive(SpecialToPrimitive(keyType.SpecialType)!.Value);
		}
		else if (keyType is IArrayTypeSymbol arrSym) {
			SerdesType? s = ProcessArray(arrSym, encountered, extras);
			if (s == null) return null;
			key = s;
		}
		else if (keyType is INamedTypeSymbol named) {
			if (named.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>") {
				SerdesType? s = ProcessDictionary(named, encountered, extras);
				if (s == null) return null;
				key = s;
			}
			else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
				string display = named.ToDisplayString(format);
				if (encountered.Add(display)) {
					SerdesType s2 = ProcessStruct(named, encountered, extras);
					extras.Add(s2);
				}
				key = new SerdesTypeQualifiedReference(named.ToDisplayString(format));
			}
			else {
				throw new Exception("this shouldn't happen");
			}
		}
		else {
			throw new Exception("this shouldn't happen");
		}
		if (IsPrimitive(valueType)) {
			value = new SerdesTypePrimitive(SpecialToPrimitive(valueType.SpecialType)!.Value);
		}
		else if (valueType is IArrayTypeSymbol arrSym) {
			SerdesType? s = ProcessArray(arrSym, encountered, extras);
			if (s == null) return null;
			value = s;
		}
		else if (valueType is INamedTypeSymbol named) {
			if (named.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>") {
				SerdesType? s = ProcessDictionary(named, encountered, extras);
				if (s == null) return null;
				value = s;
			}
			else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
				string display = named.ToDisplayString(format);
				if (encountered.Add(display)) {
					SerdesType s2 = ProcessStruct(named, encountered, extras);
					extras.Add(s2);
				}
				value = new SerdesTypeQualifiedReference(named.ToDisplayString(format));
			}
			else {
				throw new Exception("this shouldn't happen");
			}
		}
		else {
			throw new Exception("this shouldn't happen");
		}
		return new SerdesTypeDictionary(key, value, lengthBits, keyBits, valueBits, valueType.NullableAnnotation == NullableAnnotation.Annotated);
	}

	static SerdesType? ProcessEnum(INamedTypeSymbol symbol) {
		bool isAutoSized = symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "EnumAutoBitsize") != null;
		PrimitiveType? prim = SpecialToPrimitive(symbol.EnumUnderlyingType!.SpecialType);
		if (prim == null) {
			return null;
		}
		if (prim == PrimitiveType.String) {
			return null;
		}
		return new SerdesTypeEnum(prim.Value, symbol.ToDisplayString(format), symbol.GetMembers().OfType<IFieldSymbol>().Select((v) => v.Name).ToImmutableArray(), isAutoSized);
	}

	static SerdesType? ProcessArray(IArrayTypeSymbol symbol, HashSet<string> encountered, List<SerdesType?> extras, ushort? lengthBits = null, ushort? valueBits = null) {
		ITypeSymbol elementType = symbol.ElementType;
		if (elementType is not IArrayTypeSymbol && elementType is not INamedTypeSymbol && !IsPrimitive(elementType))
		{
			return null;
		}
		if (elementType is IArrayTypeSymbol arrType) {
			SerdesType? arrSer = ProcessArray(arrType, encountered, extras);
			if (arrSer == null) return null;
			SerdesType serdesArr = new SerdesTypeArray(arrSer, lengthBits, elementNullable: elementType.NullableAnnotation == NullableAnnotation.Annotated);
			return serdesArr;
		}
		else if (elementType is INamedTypeSymbol element && !IsPrimitive(element)) {
			if (element.OriginalDefinition.ToString() == "System.Collections.Generic.Dictionary<TKey, TValue>")
			{
				SerdesType? dictSer = ProcessDictionary(element, encountered, extras);
				if (dictSer == null) return null;
				SerdesType serdesArr = new SerdesTypeArray(dictSer, lengthBits, elementNullable: elementType.NullableAnnotation == NullableAnnotation.Annotated);
				return serdesArr;
			}
			else if (element.TypeKind == TypeKind.Enum) {
				SerdesType? enumSer = ProcessEnum(element);
				if (enumSer == null) return null;
				SerdesType serdesArr = new SerdesTypeArray(enumSer, lengthBits, elementNullable: elementType.NullableAnnotation == NullableAnnotation.Annotated);
				return serdesArr;
			}
			else {
				SerdesType structSer = ProcessStruct(element, encountered, extras);
				SerdesType serdesArr = new SerdesTypeArray(structSer, lengthBits, elementNullable: elementType.NullableAnnotation == NullableAnnotation.Annotated);
				return serdesArr;
			}
		}
		else if (IsPrimitive(elementType)) {
			PrimitiveType? prim = SpecialToPrimitive(elementType.SpecialType);
			if (prim == null) return null;
			SerdesType serdesArr = new SerdesTypeArray(new SerdesTypePrimitive(prim.Value), lengthBits, valueBits, elementNullable: elementType.NullableAnnotation == NullableAnnotation.Annotated);
			return serdesArr;
		}
		return null;
	}

	static string GetType(PrimitiveType type) {
		return type switch
		{
			PrimitiveType.String => "string",
			PrimitiveType.Bool => "bool",
			PrimitiveType.Byte => "byte",
			PrimitiveType.SByte => "sbyte",
			PrimitiveType.Float => "float",
			PrimitiveType.Short => "short",
			PrimitiveType.Int => "int",
			PrimitiveType.Long => "long",
			PrimitiveType.UShort => "ushort",
			PrimitiveType.UInt => "uint",
			PrimitiveType.ULong => "ulong",
			_ => throw new ArgumentException($"Unknown PrimitiveType: {type}"),
		};
	}

	static string GetCast(PrimitiveType type) {
		return $"({GetType(type)})";
	}

		static string GetPrimitiveDes(PrimitiveType type, string? sizeStr, bool async = true) {
		return type switch
		{
			PrimitiveType.String => $"{(async ? "await " : "")}%1.ReadString({sizeStr})",
			PrimitiveType.Bool => $"{(async ? "await " : "")}%1.ReadBool()",
			PrimitiveType.Byte => $"{(async ? "await " : "")}%1.ReadByte({sizeStr})",
			PrimitiveType.SByte => $"(sbyte){(async ? "await " : "")}%1.ReadByte({sizeStr})",
			PrimitiveType.Float => $"{(async ? "await " : "")}%1.ReadFloat({sizeStr})",
			PrimitiveType.Short => $"(short){(async ? "await " : "")}%1.ReadInt({sizeStr ?? "16"})",
			PrimitiveType.Int => $"{(async ? "await " : "")}%1.ReadInt({sizeStr})",
			PrimitiveType.Long => $"{(async ? "await " : "")}%1.ReadLong({sizeStr})",
			PrimitiveType.UShort => $"(ushort){(async ? "await " : "")}%1.ReadUInt({sizeStr ?? "16"})",
			PrimitiveType.UInt => $"{(async ? "await " : "")}%1.ReadUInt({sizeStr})",
			PrimitiveType.ULong => $"{(async ? "await " : "")}%1.ReadULong({sizeStr})",
			_ => throw new ArgumentException($"Unknown PrimitiveType: {type}"),
		};
	}

	static string GetPrimitiveSer(PrimitiveType type, string? sizeStr, bool async = false) {
		switch (type) {
			case PrimitiveType.String:
				if (sizeStr == null) {
					return $"{(async ? "await " : "")}%1.WriteVarString(%2)";
				}
				else {
					return $"{(async ? "await " : "")}%1.WriteString(%2{sizeStr})";
				}
			case PrimitiveType.Bool:
				return $"{(async ? "await " : "")}%1.WriteBool(%2)";
			case PrimitiveType.Byte:
				return $"{(async ? "await " : "")}%1.WriteByte(%2{sizeStr})";
			case PrimitiveType.SByte:
				return $"{(async ? "await " : "")}%1.WriteByte((byte)%2{sizeStr})";
			case PrimitiveType.Float:
				return $"{(async ? "await " : "")}%1.WriteFloat(%2{sizeStr})";
			case PrimitiveType.Short:
				return $"{(async ? "await " : "")}%1.WriteInt(%2{sizeStr ?? ", 16"})";
			case PrimitiveType.Int:
				return $"{(async ? "await " : "")}%1.WriteInt(%2{sizeStr})";
			case PrimitiveType.Long:
				return $"{(async ? "await " : "")}%1.WriteLong(%2{sizeStr})";
			case PrimitiveType.UShort:
				return $"{(async ? "await " : "")}%1.WriteUInt(%2{sizeStr ?? ", 16"})";
			case PrimitiveType.UInt:
				return $"{(async ? "await " : "")}%1.WriteUInt(%2{sizeStr})";
			case PrimitiveType.ULong:
				return $"{(async ? "await " : "")}%1.WriteULong(%2{sizeStr})";
			default:
				throw new ArgumentException($"Unknown PrimitiveType: {type}");
		}
	}

	static uint BitLength(uint n) => (uint)(n == 0 ? 1 : 32 - LeadingZeroCount(n));
	static int LeadingZeroCount(uint n)
	{
		if (n == 0) return 32;
		int count = 0;
		while ((n & 0x80000000) == 0)
		{
			count++;
			n <<= 1;
		}
		return count;
	}

	static string GetQualifiedName(SerdesType serdes) {
		return serdes switch
		{
			SerdesTypeStruct ts => ts.qualifiedName,
			SerdesTypeQualifiedReference qr => qr.qualifiedName,
			SerdesTypeArray ta => $"{GetQualifiedName(ta.elementType)}[]",
			SerdesTypeDictionary td => $"System.Collections.Generic.Dictionary<{GetQualifiedName(td.key)}, {GetQualifiedName(td.value)}>",
			SerdesTypeEnum te => te.qualifiedName,
			SerdesTypePrimitive tp => GetType(tp.primitive),
			SerdesTypeVariant => "object",
			SerdesTypeStructField sf => GetQualifiedName(sf.type),
			_ => throw new ArgumentException($"Unknown SerdesType: {serdes}"),
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string ReplaceInvalidCharacters(string str) {
		return str.Replace(" ", "_").Replace("{", "_").Replace("}", "_").Replace("=", "_").Replace(",", "_").Replace("`", "_").Replace("[", "_").Replace("]", "_").Replace(".", "_");
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string GetValidCSTostring(SerdesType serdes) {
		return ReplaceInvalidCharacters(serdes.ToString());
	}

	static IEnumerable<string> GetDes(SerdesType serdes, string appendExtra = "", uint? size = null, bool async = true) {
		switch (serdes) {
			case SerdesTypeStruct typeStruct:
				List<string> structLines = [];
				HashSet<string> typesAdded = [];
				string typeCs = GetValidCSTostring(typeStruct);
				if (!typeStruct.isRecord)
				{
					foreach (SerdesTypeStructField field in typeStruct.fields)
					{
						IEnumerable<string> fieldDes = GetDes(field.type, typeCs, field.size, async);
						if (field.useReflection)
						{
							string fieldStr = GetValidCSTostring(field.type);
							if (typesAdded.Add(typeCs))
								structLines.Add($"Type {Hash($"{appendExtra}{typeCs}Type")} = typeof({GetQualifiedName(typeStruct)});");
							if (field.isNullable)
								structLines.Add($"if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
							structLines.Add("{");
							structLines.Add($"{Tabs()}{GetQualifiedName(field.type)} {Hash($"{appendExtra}{typeCs}{fieldStr}")};");
							foreach (string sfield in fieldDes)
							{
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								structLines.Add($"{Tabs()}{sfield.Replace("%2", Hash($"{appendExtra}{typeCs}{fieldStr}"))}{end}");
							}
							structLines.Add($"{Tabs()}{Hash($"{appendExtra}{typeCs}Type")}.GetField(\"{field.name}\", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(%2, {Hash($"{appendExtra}{typeCs}{fieldStr}")});");
						}
						else
						{
							if (field.isNullable)
								structLines.Add($"if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
							structLines.Add("{");
							foreach (string sfield in fieldDes)
							{
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								structLines.Add($"{Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
							}
						}
						structLines.Add("}");
					}
				}
				else {
					List<string> extraLines = [];
					List<string> positionalFieldVariables = [];
					foreach (SerdesTypeStructField field in typeStruct.fields) {
						if (field.isPositional)
						{
							if (field.useReflection) continue;
							string fieldStr = GetValidCSTostring(field.type);
							structLines.Add($"{GetQualifiedName(field.type)}{(field.isNullable ? "?" : "")} {Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}")}{(field.isNullable ? " = null" : "")};");
							positionalFieldVariables.Add(Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}"));
							if (field.isNullable) {
								structLines.Add($"if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
							}
							structLines.Add("{");
							IEnumerable<string> fieldDes = GetDes(field.type, typeCs, field.size, async);
							foreach (string sfield in fieldDes)
							{
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								structLines.Add($"{Tabs()}{sfield.Replace("%2", Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}"))}{end}");
							}
							structLines.Add("}");
						}
						else {
							if (field.useReflection)
							{
								string fieldStr = GetValidCSTostring(field.type);
								if (typesAdded.Add(typeCs))
									extraLines.Add($"Type {Hash($"{appendExtra}{typeCs}Type")} = typeof({GetQualifiedName(typeStruct)});");
								if (field.isNullable)
									extraLines.Add($"if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
								extraLines.Add("{");
								extraLines.Add($"{Tabs()}{GetQualifiedName(field.type)} {Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}")};");
								IEnumerable<string> fieldDes = GetDes(field.type, typeCs, field.size, async);
								foreach (string sfield in fieldDes)
								{
									string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
									extraLines.Add($"{Tabs()}{sfield.Replace("%2", Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}"))}{end}");
								}
								extraLines.Add($"{Tabs()}{Hash($"{appendExtra}{typeCs}Type")}.GetField(\"{field.name}\", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(%2, {Hash($"{appendExtra}record{typeCs}{fieldStr}{field.name}")});");
							}
							else {
								if (field.isNullable)
									extraLines.Add($"if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
								extraLines.Add("{");
								IEnumerable<string> fieldDes = GetDes(field.type, typeCs, field.size);
								foreach (string sfield in fieldDes)
								{
									string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
									extraLines.Add($"{Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
								}
							}
							extraLines.Add("}");
						}
					}
					structLines.Add($"%2 = new {typeStruct.qualifiedName}({string.Join(", ", positionalFieldVariables)});");
					structLines.AddRange(extraLines);
				}
				return structLines;
			case SerdesTypeVariant typeVariant:
				List<string> variantLines = [];
				uint tvs = BitLength((uint)typeVariant.variants.Length);
				string typeVarV = Hash($"{appendExtra}{GetValidCSTostring(typeVariant)}{string.Join("_", typeVariant.variants.Select(GetValidCSTostring))}");
				string typeVarName = Hash($"{appendExtra}{typeVarV}_variant");
				variantLines.Add($"object {typeVarV} = null;");
				variantLines.Add($"uint {typeVarName} = {GetPrimitiveDes(PrimitiveType.UInt, $"{tvs}", async)};");
				for (int i = 0; i < typeVariant.variants.Length; i++) {
					SerdesType var = typeVariant.variants[i];
					IEnumerable<string> varDes = var is SerdesTypeStructField sf ? GetDes(sf.type, appendExtra + "_variant", sf.size, async) : GetDes(var, async: async);
					variantLines.Add($"if ({typeVarName} == {i}) {{");
					foreach (string sfield in varDes) {
						string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
						variantLines.Add($"{Tabs()}{sfield.Replace("%2", typeVarV)}{end}");
					}
					variantLines.Add("}");
				}
				variantLines.Add($"%2 = {typeVarV};");
				return variantLines;
			case SerdesTypeArray typeArray:
				List<string> arrLines = [];
				string arrTs = GetValidCSTostring(typeArray);
				arrLines.Add($"int {Hash($"{appendExtra}arr{arrTs}length")} = {GetPrimitiveDes(PrimitiveType.Int, typeArray.lengthSize != null ? $"{typeArray.lengthSize}" : "", async)};");
				arrLines.Add($"{GetQualifiedName(typeArray)} {Hash($"{appendExtra}{arrTs}")} = new {GetQualifiedName(typeArray.elementType)}[{appendExtra}arr{arrTs}length]");
				string arrElementStr = GetValidCSTostring(typeArray.elementType);
				arrLines.Add($"for (int {Hash($"{appendExtra}i{arrElementStr}")} = 0; {Hash($"{appendExtra}i{arrElementStr}")} < {Hash($"{appendExtra}arr{arrTs}length")}; {Hash($"{appendExtra}i{arrElementStr}")}++) {{");
				IEnumerable<string> eArrDes = GetDes(typeArray.elementType, arrTs, typeArray.valueSize, async);
				if (typeArray.elementNullable)
				{
					arrLines.Add($"{Tabs()}{GetQualifiedName(typeArray.elementType)}? {Hash($"{appendExtra}{arrElementStr}_arrElement")} = null;");
					arrLines.Add($"{Tabs()}if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
					arrLines.Add($"{Tabs()}{{");
					foreach (string line in eArrDes) {
						arrLines.Add($"{Tabs(typeArray.elementNullable ? 2 : 1)}{line.Replace("%2", Hash($"{appendExtra}{arrElementStr}_arrElement"))}");
					}
					arrLines.Add($"{Tabs()}}}");
					arrLines.Add($"{Tabs()}{appendExtra}{arrTs}[{appendExtra}i{arrElementStr}] = {Hash($"{appendExtra}{arrElementStr}_arrElement")};");
				}
				else {
					foreach (string line in eArrDes) {
						arrLines.Add($"{Tabs(typeArray.elementNullable ? 2 : 1)}{line.Replace("%2", $"{Hash($"{appendExtra}{arrTs}")}[{Hash($"{appendExtra}i{arrElementStr}")}]")}");
					}
				}
				arrLines.Add("}");
				arrLines.Add($"%2 = {Hash($"{appendExtra}{arrTs}")}");
				return arrLines;
			case SerdesTypeDictionary typeDictionary:
				List<string> dictLines = [];
				string dictTs = GetValidCSTostring(typeDictionary);
				dictLines.Add($"int {Hash($"{appendExtra}dict{dictTs}length")} = {GetPrimitiveDes(PrimitiveType.Int, typeDictionary.lengthSize != null ? $"{typeDictionary.lengthSize}" : "")}");
				dictLines.Add($"{GetQualifiedName(typeDictionary)} {Hash($"{appendExtra}{dictTs}")} = []");
				string dictKeyStr = GetValidCSTostring(typeDictionary.key);
				string dictValStr = GetValidCSTostring(typeDictionary.value);
				dictLines.Add($"for (int {Hash($"{appendExtra}i{dictKeyStr}{dictValStr}")} = 0; {Hash($"{appendExtra}i{dictKeyStr}{dictValStr}")} < {Hash($"{appendExtra}dict{dictTs}length")}; {Hash($"{appendExtra}i{dictKeyStr}{dictValStr}")}++) {{");
				IEnumerable<string> keyDes = GetDes(typeDictionary.key, dictTs, typeDictionary.keySize, async);
				IEnumerable<string> valueDes = GetDes(typeDictionary.value, dictTs, typeDictionary.valueSize, async);
				foreach (string line in keyDes) {
					dictLines.Add($"{Tabs()}{line.Replace("%2", $"{GetQualifiedName(typeDictionary.key)} {Hash($"{appendExtra}{dictKeyStr}")}")}");
				}
				if (typeDictionary.valueNullable) {
					dictLines.Add($"{Tabs()}{GetQualifiedName(typeDictionary.key)}? {Hash($"{appendExtra}{dictValStr}_nullable")} = null;");
					dictLines.Add($"{Tabs()}if ({GetPrimitiveDes(PrimitiveType.Bool, null, async)})");
					dictLines.Add($"{Tabs()}{{");
					foreach (string line in valueDes) {
						dictLines.Add($"{Tabs(2)}{line.Replace("%2", Hash($"{appendExtra}{dictValStr}_nullable"))}");
					}
					dictLines.Add($"{Tabs()}}}");
					dictLines.Add($"{Hash($"{appendExtra}{dictTs}")}[{Hash($"{appendExtra}{dictKeyStr}_nullable")}] = {Hash($"{appendExtra}{dictValStr}_nullable")}");
				}
				else {
					foreach (string line in valueDes) {
						dictLines.Add($"{Tabs()}{line.Replace("%2", $"{GetQualifiedName(typeDictionary.value)} {Hash($"{appendExtra}{dictValStr}")}")}");
					}
					dictLines.Add($"{Hash($"{appendExtra}{dictTs}")}[{Hash($"{appendExtra}{dictKeyStr}")}] = {Hash($"{appendExtra}{dictValStr}")}");
				}
				dictLines.Add("}");
				dictLines.Add($"%2 = {Hash($"{appendExtra}{dictTs}")}");
				return dictLines;
			case SerdesTypeEnum typeEnum:
				if (typeEnum.autoSize)
				{
					uint bitSize = BitLength((uint)typeEnum.values.Length);
					return [$"%2 = ({GetQualifiedName(typeEnum)}){GetPrimitiveDes(typeEnum.primitive, $"{bitSize}", async)}"];
				}
				else {
					return [$"%2 = ({GetQualifiedName(typeEnum)}){GetPrimitiveDes(typeEnum.primitive, null, async)}"];
				}
			case SerdesTypePrimitive typePrimitive:
				string? sizeStr = size != null ? $"{size}" : null;
				string des = GetPrimitiveDes(typePrimitive.primitive, sizeStr, async);
				return [$"%2 = {des}"];
			case SerdesTypeQualifiedReference typeReference:
				string underscored = typeReference.qualifiedName.Replace(".", "_");
				return [
					$"%2 = {(async ? "await " : "")}{underscored}SerDes.Deserialize(%1)"
				];
			default:
				return [];
		}
	}

	static IEnumerable<string> GetSer(SerdesType serdes, uint? size = null, bool async = false) {
		switch (serdes)
		{
			case SerdesTypeStruct typeStruct:
				List<string> structLines = [];
				HashSet<string> typesAdded = [];
				foreach (SerdesTypeStructField field in typeStruct.fields)
				{
					if (field.useReflection)
					{
						string fieldStr = GetValidCSTostring(typeStruct);
						if (typesAdded.Add(fieldStr))
							structLines.Add($"Type {fieldStr}Type = typeof({GetQualifiedName(typeStruct)});");
						if (field.isNullable)
						{
							structLines.Add($"{GetPrimitiveSer(PrimitiveType.Bool, null, async).Replace("%2", $"{fieldStr}Type.GetField(\"{field.name}\", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(%2) != null")};");
							structLines.Add($"if ({fieldStr}Type.GetField(\"{field.name}\", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(%2) != null)"); ;
						}
						structLines.Add("{");
						IEnumerable<string> fieldSer = GetSer(field.type, field.size, async);
						foreach (string sfield in fieldSer)
						{
							string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
							structLines.Add($"{Tabs()}{sfield.Replace("%2", $"({GetQualifiedName(field.type)}){fieldStr}Type.GetField(\"{field.name}\", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(%2)")}{end}");
						}
					}
					else
					{
						if (field.isNullable)
						{
							structLines.Add($"{GetPrimitiveSer(PrimitiveType.Bool, null, async).Replace("%2", $"%2.{field.name} != null")};");
							structLines.Add($"if (%2.{field.name} != null)");
						}
						structLines.Add("{");
						IEnumerable<string> fieldSer = GetSer(field.type, field.size, async);
						foreach (string sfield in fieldSer)
						{
							string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
							structLines.Add($"{Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
						}
					}
					structLines.Add("}");
				}
				return structLines;
			case SerdesTypeVariant typeVariant:
				List<string> variantLines = [];
				uint tvs = BitLength((uint)typeVariant.variants.Length);
				for (int i = 0; i < typeVariant.variants.Length; i++)
				{
					SerdesType var = typeVariant.variants[i];
					IEnumerable<string> varSer = var is SerdesTypeStructField sf ? GetSer(sf.type, sf.size, async) : GetSer(var, async: async);
					string underscored = ReplaceInvalidCharacters(GetQualifiedName(var)) + "_variant";
					variantLines.Add($"if (%2 is {GetQualifiedName(var)} {underscored}) {{");
					variantLines.Add($"{Tabs()}{(async ? "await " : "")}%1.WriteUInt({i}, {tvs});");
					foreach (string sfield in varSer) {
						string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
						variantLines.Add($"{Tabs()}{sfield.Replace("%2", underscored)}{end}");
					}
					variantLines.Add("}");
				}
				return variantLines;
			case SerdesTypeArray typeArray:
				List<string> arrLines = [];
				arrLines.Add($"{(async ? "await " : "")}%1.WriteInt(%2.Length{(typeArray.lengthSize != null ? $", {typeArray.lengthSize}" : "")})");
				string elementKeyStr = Hash(GetValidCSTostring(typeArray.elementType));
				arrLines.Add($"foreach (var arrK{elementKeyStr} in %2) {{");
				IEnumerable<string> itemSer = GetSer(typeArray.elementType, typeArray.valueSize);
				if (typeArray.elementNullable) {
					arrLines.Add($"{Tabs()}{GetPrimitiveSer(PrimitiveType.Bool, null, async).Replace("%2", $"arrK{elementKeyStr} != null")};");
				}
				foreach (string line in itemSer) {
					arrLines.Add($"{Tabs()}{line.Replace("%2", $"arrK{elementKeyStr}")}");
				}
				arrLines.Add("}");
				return arrLines;
			case SerdesTypeDictionary typeDict:
				List<string> dictLines = [];
				dictLines.Add($"{(async ? "await " : "")}%1.WriteInt(%2.Count{(typeDict.lengthSize != null ? $", {typeDict.lengthSize}" : "")})");
				string dictKeyStr = Hash(GetValidCSTostring(typeDict.key));
				string dictValStr = Hash(GetValidCSTostring(typeDict.value));
				dictLines.Add($"foreach (var dictKv{dictKeyStr}{dictValStr} in %2) {{");
				IEnumerable<string> keySer = GetSer(typeDict.key, typeDict.keySize, async);
				IEnumerable<string> valueSer = GetSer(typeDict.value, typeDict.valueSize, async);
				foreach (string line in keySer) {
					dictLines.Add($"{Tabs()}{line.Replace("%2", $"dictKv{dictKeyStr}{dictValStr}.Key")}");
				}
				if (typeDict.valueNullable)
					dictLines.Add($"{Tabs()}{GetPrimitiveSer(PrimitiveType.Bool, null, async).Replace("%2", $"dictKv{dictKeyStr}{dictValStr}.Value != null")};");
				foreach (string line in valueSer) {
					dictLines.Add($"{Tabs()}{line.Replace("%2", $"dictKv{dictKeyStr}{dictValStr}.Value")}");
				}
				dictLines.Add("}");
				return dictLines;
			case SerdesTypeEnum typeEnum:
				if (typeEnum.autoSize) {
					uint bitSize = BitLength((uint)typeEnum.values.Length);
					string primSer = GetPrimitiveSer(typeEnum.primitive, $", {bitSize}", async);
					return [primSer.Replace("%2", $"{GetCast(typeEnum.primitive)}%2")];
				}
				else {
					string primSer = GetPrimitiveSer(typeEnum.primitive, null, async);
					return [primSer.Replace("%2", $"{GetCast(typeEnum.primitive)}%2")];
				}
			case SerdesTypePrimitive typePrimitive:
				string? sizeStr = size != null ? $", {size}" : null;
				string ser = GetPrimitiveSer(typePrimitive.primitive, sizeStr, async);
				return [ser];
			case SerdesTypeQualifiedReference typeReference:
				return [$"{(async ? "await " : "")}{typeReference.qualifiedName.Replace(".", "_")}SerDes.Serialize(%2, %1)"];
			default:
				return [];
		}
	}

	public static void GenerateSerDes(SourceProductionContext prodContext, IEnumerable<SerdesType> serdes, bool serializeAsync = false, bool deserializeAsync = true)
	{
		SerdesTypeStruct[] structs = [.. serdes.Select((v) => v is SerdesTypeStruct str ? str : null).Where(c => c is not null)!];
		SerdesTypeEnum[] enums = [.. serdes.Select((v) => v is SerdesTypeEnum str ? str : null).Where(c => c is not null)!];
		{
			foreach (SerdesTypeStruct struc in structs)
			{
				string underscored = struc.qualifiedName.Replace(".", "_");
				StringBuilder sb = new();
				sb.AppendLine("using System;");
				sb.AppendLine("using tairasoul.unity.common.bits;");
				sb.AppendLine("using System.CodeDom.Compiler;");
				sb.AppendLine("using System.Reflection;");
				sb.AppendLine("using System.Threading.Tasks;");
				sb.AppendLine("namespace tairasoul.unity.common.serdes;");
				sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
				sb.AppendLine($"class {underscored}SerDes {{");
				if (serializeAsync)
					sb.AppendLine($"{Tabs()}public static async Task Serialize({struc.qualifiedName} {underscored}, BitWriterAsync writer) {{");
				else
					sb.AppendLine($"{Tabs()}public static void Serialize({struc.qualifiedName} {underscored}, BitWriter writer) {{");
				IEnumerable<string> serLines = GetSer(struc, async: serializeAsync);
				foreach (string ser in serLines)
				{
					sb.AppendLine($"{Tabs(2)}{ser.Replace("%1", "writer").Replace("%2", underscored)}");
				}
				sb.AppendLine($"{Tabs()}}}");
				if (deserializeAsync)
					sb.AppendLine($"{Tabs()}public static async Task<{struc.qualifiedName}> Deserialize(BitReaderAsync reader) {{");
				else
					sb.AppendLine($"{Tabs()}public static {struc.qualifiedName} Deserialize(BitReader reader) {{");
				if (!struc.isRecord)
					sb.AppendLine($"{Tabs(2)}{struc.qualifiedName} instance{underscored}async = default;");
				IEnumerable<string> desLines = GetDes(struc, async: deserializeAsync);
				if (!desLines.Any((v) => v.Contains("%2 =")))
					desLines = [.. desLines, $"return instance{underscored}async;"];
				foreach (string des in desLines)
				{
					sb.AppendLine($"{Tabs(2)}{des.Replace("%1", "reader").Replace("%2 =", "return").Replace("%2", $"instance{underscored}async")}");
				}
				sb.AppendLine($"{Tabs()}}}");
				sb.AppendLine("}");
				prodContext.AddSource($"serdes/{underscored}.g.cs", sb.ToString());
			}
		}
		{
			StringBuilder sb = new();
			sb.AppendLine("using System;");
			sb.AppendLine("using tairasoul.unity.common.bits;");
			sb.AppendLine("using System.CodeDom.Compiler;");
			sb.AppendLine("using System.Reflection;");
			sb.AppendLine("using System.Threading.Tasks;");
			sb.AppendLine("namespace tairasoul.unity.common.serdes;");
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine("class SerDesMap {");
			if (serializeAsync)
				sb.AppendLine($"{Tabs()}public static async Task Serialize(object value, BitWriterAsync writer) {{");
			else
				sb.AppendLine($"{Tabs()}public static void Serialize(object value, BitWriter writer) {{");
			foreach (SerdesTypeStruct struc in structs)
			{
				string underscored = struc.qualifiedName.Replace(".", "_");
				SerdesTypeQualifiedReference refer = new(struc.qualifiedName);
				IEnumerable<string> ser = GetSer(refer, async: serializeAsync);
				sb.AppendLine($"{Tabs(2)}if (value is {struc.qualifiedName} {underscored}) {{");
				foreach (string serStr in ser)
				{
					string end = !serStr.EndsWith("{") && !serStr.EndsWith("}") && !serStr.EndsWith(";") ? ";" : "";
					sb.AppendLine($"{Tabs(3)}{serStr.Replace("%1", "writer").Replace("%2", underscored)}{end}");
				}
				sb.AppendLine($"{Tabs(3)}return;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			foreach (SerdesTypeEnum tenum in enums)
			{
				string underscored = tenum.qualifiedName.Replace(".", "_");
				IEnumerable<string> ser = GetSer(tenum, async: serializeAsync);
				sb.AppendLine($"{Tabs(2)}if (value is {tenum.qualifiedName} {underscored}) {{");
				foreach (string serStr in ser)
				{
					string end = !serStr.EndsWith("{") && !serStr.EndsWith("}") && !serStr.EndsWith(";") ? ";" : "";
					sb.AppendLine($"{Tabs(3)}{serStr.Replace("%1", "writer").Replace("%2", underscored)}{end}");
				}
				sb.AppendLine($"{Tabs(3)}return;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			sb.AppendLine($"{Tabs()}}}");
			if (deserializeAsync)
				sb.AppendLine($"{Tabs()}public static async Task<object> Deserialize(Type type, BitReaderAsync reader) {{");
			else
				sb.AppendLine($"{Tabs()}public static object Deserialize(Type type, BitReader reader) {{");
			foreach (SerdesTypeStruct struc in structs)
			{
				string underscored = struc.qualifiedName.Replace(".", "_");
				SerdesTypeQualifiedReference refer = new(struc.qualifiedName);
				List<string> des = [.. GetDes(refer, async: deserializeAsync)];
				des[des.Count - 1] = des[des.Count - 1].Replace("%2 =", "return");
				sb.AppendLine($"{Tabs(2)}if (type == typeof({struc.qualifiedName})) {{");
				foreach (string desStr in des)
				{
					string end = !desStr.EndsWith("{") && !desStr.EndsWith("}") && !desStr.EndsWith(";") ? ";" : "";
					sb.AppendLine($"{Tabs(3)}{desStr.Replace("%1", "reader")}{end}");
				}
				sb.AppendLine($"{Tabs(2)}}}");
			}
			foreach (SerdesTypeEnum tenum in enums)
			{
				List<string> des = [.. GetDes(tenum, async: deserializeAsync)];
				des[des.Count - 1] = des[des.Count - 1].Replace("%2 =", "return");
				sb.AppendLine($"{Tabs(2)}if (type == typeof({tenum.qualifiedName})) {{");
				foreach (string desStr in des)
				{
					string end = !desStr.EndsWith("{") && !desStr.EndsWith("}") && !desStr.EndsWith(";") ? ";" : "";
					sb.AppendLine($"{Tabs(3)}{desStr.Replace("%1", "reader")}{end}");
				}
				sb.AppendLine($"{Tabs(2)}}}");
			}
			sb.AppendLine($"{Tabs(2)}throw new System.Exception($\"this should not happen, could not deserialize type {{type}}\");");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource("serdes/map.g.cs", sb.ToString());
		}
	}
}