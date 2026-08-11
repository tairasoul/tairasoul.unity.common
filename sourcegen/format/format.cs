using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace tairasoul.unity.common.sourcegen.format;

using SerdeReturn = (IEnumerable<string> func, IEnumerable<string> @class, IEnumerable<string> attributes);

static class FormatGen
{
	static readonly SymbolDisplayFormat format = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
	);

	public static bool Predicate(SyntaxNode node)
	{
		if (node is InvocationExpressionSyntax invoc)
		{
			if (invoc.Expression is MemberAccessExpressionSyntax ma)
			{
				if (ma.Name is IdentifierNameSyntax { Identifier.Text: "Read" or "Write" } || ma.Name is GenericNameSyntax { Identifier.Text: "Read" or "Write" })
					return true;
			}
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static Primitive? SpecialToPrimitive(SpecialType special)
	{
		return special switch
		{
			SpecialType.System_Byte => Primitive.Byte,
			SpecialType.System_SByte => Primitive.SByte,
			SpecialType.System_Int16 => Primitive.Short,
			SpecialType.System_UInt16 => Primitive.UShort,
			SpecialType.System_Int32 => Primitive.Int,
			SpecialType.System_UInt32 => Primitive.UInt,
			SpecialType.System_Single => Primitive.Float,
			SpecialType.System_Int64 => Primitive.Long,
			SpecialType.System_UInt64 => Primitive.ULong,
			SpecialType.System_Double => Primitive.Double,
			SpecialType.System_String => Primitive.String,
			SpecialType.System_Boolean => Primitive.Bool,
			_ => null
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPrimitive(SpecialType special)
	{
		return SpecialToPrimitive(special) != null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPrimitive(ITypeSymbol symbol)
	{
		return IsPrimitive(symbol.SpecialType);
	}

	public static FormatItem?[] TransformUnchecked(SyntaxNode syntax, SemanticModel semantic, CancellationToken ct)
	{
		ISymbol? symbol = semantic.GetSymbolInfo(syntax).Symbol;
		if (symbol == null) return [];
		if (symbol.ToDisplayString(format) == "System.Object") return [];
		HashSet<string> encountered = [];
		List<FormatItem> extras = [];
		if (symbol is INamedTypeSymbol named)
		{
			if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class)
			{
				return [ProcessStruct(named, semantic, encountered, extras), .. extras];
			}
		}
		return [];
	}

	public static FormatItem?[] Transform(SyntaxNode syntax, SemanticModel semantic, CancellationToken ct)
	{
		var ma = (InvocationExpressionSyntax)syntax;
		var methodSymbol = semantic.GetSymbolInfo(ma.Expression).Symbol as IMethodSymbol;
		var targetSymbol = methodSymbol?.ReceiverType;

		string s = targetSymbol?.ToDisplayString(format);

		if (s != "tairasoul.unity.common.format.FormatReader" && s != "tairasoul.unity.common.format.FormatWriter")
			return [];

		if (methodSymbol.TypeArguments.Length <= 0)
			return [];

		ISymbol? symbol = methodSymbol.TypeArguments[0];
		if (symbol is null) return [];
		if (symbol.ToDisplayString(format) == "System.Object") return [];
		HashSet<string> encountered = [];
		List<FormatItem> extras = [];
		if (symbol is INamedTypeSymbol named)
		{
			if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class)
			{
				return [ProcessStruct(named, semantic, encountered, extras), .. extras];
			}
		}
		return [];
	}

	static FormatItem? ProcessStruct(INamedTypeSymbol symbol, SemanticModel model, HashSet<string> encountered, List<FormatItem> extras)
	{
		var dictInterface = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
		var nullableType = model.Compilation.GetTypeByMetadataName("System.Nullable`1");
		List<FormatStructField> fields = [];
		bool unmanaged = symbol.IsValueType;
		foreach (IFieldSymbol field in symbol.GetMembers().OfType<IFieldSymbol>().Where(p => !p.IsImplicitlyDeclared))
		{
			if (field.IsConst || field.IsReadOnly) continue;
			if (field.Type is INamedTypeSymbol named && named.SpecialType == SpecialType.None)
			{
				if (named.AllInterfaces.Any((i) => i.OriginalDefinition.Equals(dictInterface, SymbolEqualityComparer.Default)))
				{
					unmanaged = false;
					FormatItem? dict = ProcessDictionary(named, model, encountered, extras);
					if (dict == null) return null;
					fields.Add(new FormatStructField(field.Name, dict, field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
				else if (named.TypeKind == TypeKind.Interface) {
					unmanaged = false;
					string name = named.ToDisplayString(format);
					if (encountered.Add(name)) {
						FormatItem? @interface = ProcessInterface(named, model, encountered, extras);
						if (@interface == null) return null;
						extras.Add(@interface);
					}
					FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
					fields.Add(new FormatStructField(field.Name, qual, field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
				else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class)
				{
					if (named.TypeKind == TypeKind.Class) unmanaged = false;
					string name = named.ToDisplayString(format);
					if (named.OriginalDefinition.Equals(nullableType, SymbolEqualityComparer.Default)) {
						ITypeSymbol nullableTypeArgument = named.TypeArguments.First();
						if (IsPrimitive(nullableTypeArgument)) {
							Primitive primitive = SpecialToPrimitive(nullableTypeArgument.SpecialType)!.Value;
							fields.Add(new FormatStructField(field.Name, new FormatPrimitive(primitive), field.DeclaredAccessibility != Accessibility.Public, false, true));
						}
						else if (named.TypeKind == TypeKind.Enum) {
							unmanaged = false;
							FormatItem @enum = ProcessEnum(named);
							fields.Add(new FormatStructField(field.Name, @enum, field.DeclaredAccessibility != Accessibility.Public, false, true));
						}
						else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class) {
							if (named.TypeKind == TypeKind.Class) unmanaged = false;
							if (encountered.Add(name)) {
								FormatStruct? @struct = (FormatStruct?)ProcessStruct(named, model, encountered, extras);
								if (@struct == null) return null;
								if (!@struct.isUnmanaged) unmanaged = false;
								extras.Add(@struct);
							}
							FormatQualifiedReference qualref = new(name, named.ContainingNamespace.ToDisplayString(format));
							fields.Add(new FormatStructField(field.Name, qualref, field.DeclaredAccessibility != Accessibility.Public, false, true));
						}
						continue;
					}
					if (encountered.Add(name)) {
						FormatStruct? @struct = (FormatStruct?)ProcessStruct(named, model, encountered, extras);
						if (@struct == null) return null;
						if (!@struct.isUnmanaged)
							unmanaged = false;
						extras.Add(@struct);
					}
					FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
					fields.Add(new FormatStructField(field.Name, qual, field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
				else if (named.TypeKind == TypeKind.Enum)
				{
					// if (named.EnumUnderlyingType != null && !IsPrimitive(named.EnumUnderlyingType))
						unmanaged = false;
					FormatItem @enum = ProcessEnum(named);
					fields.Add(new FormatStructField(field.Name, @enum, field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
			}
			else if (field.Type is IArrayTypeSymbol arrSym)
			{
				FormatArray? arr = (FormatArray?)ProcessArray(arrSym, model, encountered, extras);
				if (arr == null) return null;
				if (!IsUnmanaged(arr.element))
					unmanaged = false;
				fields.Add(new FormatStructField(field.Name, arr, field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
			}
			else if (IsPrimitive(field.Type))
			{
				var prim = SpecialToPrimitive(field.Type.SpecialType)!.Value;
				if (prim == Primitive.String)
					unmanaged = false;
				fields.Add(new FormatStructField(field.Name, new FormatPrimitive(prim), field.DeclaredAccessibility != Accessibility.Public, false, field.NullableAnnotation == NullableAnnotation.Annotated));
			}
		}
		if (symbol.IsRecord)
		{
			foreach (IPropertySymbol field in symbol.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsImplicitlyDeclared))
			{
				if (field.IsReadOnly) continue;
				if (field.Type is INamedTypeSymbol named && named.SpecialType == SpecialType.None)
				{
					if (named.AllInterfaces.Any((i) => i.OriginalDefinition.Equals(dictInterface, SymbolEqualityComparer.Default)))
					{
						unmanaged = false;
						FormatItem? dict = ProcessDictionary(named, model, encountered, extras);
						if (dict == null) return null;
						fields.Add(new FormatStructField(field.Name, dict, field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
					}
					else if (named.TypeKind == TypeKind.Interface) {
						unmanaged = false;
						string name = named.ToDisplayString(format);
						if (encountered.Add(name)) {
							FormatItem? @interface = ProcessInterface(named, model, encountered, extras);
							if (@interface == null) return null;
							extras.Add(@interface);
						}
						FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
						fields.Add(new FormatStructField(field.Name, qual, field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
					}
					else if (named.TypeKind == TypeKind.Struct || named.TypeKind == TypeKind.Class)
					{
						if (named.TypeKind == TypeKind.Class) unmanaged = false;
						string name = named.ToDisplayString(format);
						if (encountered.Add(name)) {
							FormatStruct? @struct = (FormatStruct?)ProcessStruct(named, model, encountered, extras);
							if (@struct == null) return null;
							if (!@struct.isUnmanaged)
								unmanaged = false;
							extras.Add(@struct);
						}
						FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
						fields.Add(new FormatStructField(field.Name, qual, field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
					}
					else if (named.TypeKind == TypeKind.Enum)
					{
						if (named.EnumUnderlyingType != null && !IsPrimitive(named.EnumUnderlyingType))
							unmanaged = false;
						FormatEnum @enum = ProcessEnum(named);
						if (@enum.compacted)
							unmanaged = false;
						fields.Add(new FormatStructField(field.Name, @enum, field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
					}
				}
				else if (field.Type is IArrayTypeSymbol arrSym)
				{
					unmanaged = false;
					FormatItem? arr = ProcessArray(arrSym, model, encountered, extras);
					if (arr == null) return null;
					fields.Add(new FormatStructField(field.Name, arr, field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
				else if (IsPrimitive(field.Type))
				{
					var prim = SpecialToPrimitive(field.Type.SpecialType)!.Value;
					if (prim == Primitive.String)
						unmanaged = false;
					fields.Add(new FormatStructField(field.Name, new FormatPrimitive(prim), field.DeclaredAccessibility != Accessibility.Public, true, field.NullableAnnotation == NullableAnnotation.Annotated));
				}
			}
		}
		return new FormatStruct(symbol.ToDisplayString(format), symbol.ContainingNamespace.ToDisplayString(format), fields.ToImmutableArray(), symbol.IsRecord, symbol.DeclaredAccessibility == Accessibility.Public, unmanaged, symbol.TypeKind == TypeKind.Class);
	}

	static FormatInterface? ProcessInterface(INamedTypeSymbol symbol, SemanticModel model, HashSet<string> encountered, List<FormatItem> extras) 
	{
		var implementing = model.Compilation.SyntaxTrees
			.Select(tree => model.Compilation.GetSemanticModel(tree).GetDeclaredSymbol(tree.GetRoot())?.ContainingSymbol as INamedTypeSymbol)
			.Where(type => type!.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct)
			.Where(type => type?.AllInterfaces.Contains(symbol) == true)
			.Where(type => type != null);
		List<FormatQualifiedReference> quals = [];
		foreach (var impl in implementing) {
			string name = impl!.ToDisplayString(format);
			if (encountered.Add(name)) {
				FormatItem? @struct = ProcessStruct(impl, model, encountered, extras);
				if (@struct == null) return null;
				extras.Add(@struct);
			}
			quals.Add(new(name, impl.ContainingNamespace.ToDisplayString(format)));
		}
		return new FormatInterface(quals.ToImmutableArray(), symbol.ToDisplayString(format));
	}

	static FormatDictionary? ProcessDictionary(INamedTypeSymbol symbol, SemanticModel model, HashSet<string> encountered, List<FormatItem> extras)
	{
		var dictInterface = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
		if (symbol.TypeArguments.Length < 2 ||
			symbol.TypeArguments[0] is not { } keyType ||
			symbol.TypeArguments[1] is not { } valueType)
			throw new Exception("this shouldn't happen");
		FormatItem key;
		FormatItem value;
		if (IsPrimitive(keyType))
		{
			key = new FormatPrimitive(SpecialToPrimitive(keyType.SpecialType)!.Value);
		}
		else if (keyType is IArrayTypeSymbol arrSym)
		{
			FormatItem? arr = ProcessArray(arrSym, model, encountered, extras);
			if (arr == null) return null;
			key = arr;
		}
		else
		{
			INamedTypeSymbol named = (INamedTypeSymbol)keyType;
			if (named.AllInterfaces.Any(i => i.OriginalDefinition.Equals(dictInterface, SymbolEqualityComparer.Default)))
			{
				FormatItem? k = ProcessDictionary(named, model, encountered, extras);
				if (k == null) return null;
				key = k;
			}
			else if (named.TypeKind == TypeKind.Enum) {
				FormatItem? @enum = ProcessEnum(named);
				if (@enum == null) return null;
				key = @enum;
			}
			else if (named.TypeKind == TypeKind.Interface) {
				string name = named.ToDisplayString(format);
				if (encountered.Add(name)) {
					FormatItem? @interface = ProcessInterface(named, model, encountered, extras);
					if (@interface == null) return null;
					extras.Add(@interface);
				}
				FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
				key = qual;
			}
			else
			{
				string display = named.ToDisplayString(format);
				if (encountered.Add(display))
				{
					FormatItem? f = ProcessStruct(named, model, encountered, extras);
					if (f == null) return null;
					extras.Add(f);
				}
				key = new FormatQualifiedReference(display, named.ContainingNamespace.ToDisplayString(format));
			}
		}
		if (IsPrimitive(valueType))
		{
			value = new FormatPrimitive(SpecialToPrimitive(valueType.SpecialType)!.Value);
		}
		else if (valueType is IArrayTypeSymbol arrSym)
		{
			FormatItem? arr = ProcessArray(arrSym, model, encountered, extras);
			if (arr == null) return null;
			value = arr;
		}
		else
		{
			INamedTypeSymbol named = (INamedTypeSymbol)valueType;
			if (named.AllInterfaces.Any(i => i.OriginalDefinition.Equals(dictInterface, SymbolEqualityComparer.Default)))
			{
				FormatItem? v = ProcessDictionary(named, model, encountered, extras);
				if (v == null) return null;
				value = v;
			}
			else if (named.TypeKind == TypeKind.Enum) {
				FormatItem? @enum = ProcessEnum(named);
				if (@enum == null) return null;
				value = @enum;
			}
			else if (named.TypeKind == TypeKind.Interface) {
				string name = named.ToDisplayString(format);
				if (encountered.Add(name)) {
					FormatItem? @interface = ProcessInterface(named, model, encountered, extras);
					if (@interface == null) return null;
					extras.Add(@interface);
				}
				FormatQualifiedReference qual = new(name, named.ContainingNamespace.ToDisplayString(format));
				value = qual;
			}
			else
			{
				string display = named.ToDisplayString(format);
				if (encountered.Add(display))
				{
					FormatItem? f = ProcessStruct(named, model, encountered, extras);
					if (f == null) return null;
					extras.Add(f);
				}
				value = new FormatQualifiedReference(display, named.ContainingNamespace.ToDisplayString(format));
			}
		}
		return new FormatDictionary(key, value, valueType.NullableAnnotation == NullableAnnotation.Annotated, symbol.ToDisplayString(format));
	}

	static FormatEnum ProcessEnum(INamedTypeSymbol symbol)
	{
		bool compacted = false;
		return new FormatEnum(symbol.ToDisplayString(format), compacted, SpecialToPrimitive(symbol.EnumUnderlyingType!.SpecialType)!.Value, symbol.GetMembers().OfType<IFieldSymbol>().Select((v) => v.Name).ToImmutableArray());
	}

	static FormatArray? ProcessArray(IArrayTypeSymbol symbol, SemanticModel model, HashSet<string> encountered, List<FormatItem> extras)
	{
		var dictInterface = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
		ITypeSymbol elementType = symbol.ElementType;
		if (elementType is IArrayTypeSymbol arrType)
		{
			FormatItem? arr = ProcessArray(arrType, model, encountered, extras);
			if (arr == null) return null;
			return new FormatArray(arr, elementType.NullableAnnotation == NullableAnnotation.Annotated);
		}
		else if (IsPrimitive(elementType))
		{
			Primitive? primitive = SpecialToPrimitive(elementType.SpecialType);
			if (primitive == null) return null;
			return new FormatArray(new FormatPrimitive(primitive.Value), elementType.NullableAnnotation == NullableAnnotation.Annotated);
		}
		else if (elementType is INamedTypeSymbol element)
		{
			if (element.AllInterfaces.Any(i => i.OriginalDefinition.Equals(dictInterface, SymbolEqualityComparer.Default)))
			{
				FormatItem? dict = ProcessDictionary(element, model, encountered, extras);
				if (dict == null) return null;
				return new FormatArray(dict, elementType.NullableAnnotation == NullableAnnotation.Annotated);
			}
			else if (element.TypeKind == TypeKind.Interface) {
				string name = element.ToDisplayString(format);
				if (encountered.Add(name)) {
					FormatItem? @interface = ProcessInterface(element, model, encountered, extras);
					if (@interface == null) return null;
					extras.Add(@interface);
				}
				FormatQualifiedReference qual = new(name, element.ContainingNamespace.ToDisplayString(format));
				return new FormatArray(qual, element.NullableAnnotation == NullableAnnotation.Annotated);
			}
			else if (element.TypeKind == TypeKind.Enum)
			{
				FormatItem? @enum = ProcessEnum(element);
				if (@enum == null) return null;
				return new FormatArray(@enum, element.NullableAnnotation == NullableAnnotation.Annotated);
			}
			else
			{
				FormatItem? @struct = ProcessStruct(element, model, encountered, extras);
				if (@struct == null) return null;
				return new FormatArray(@struct, element.NullableAnnotation == NullableAnnotation.Annotated);
			}
		}
		else
		{
			return null;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string GetType(Primitive type)
	{
		return type switch
		{
			Primitive.String => "string",
			Primitive.Bool => "bool",
			Primitive.Byte => "byte",
			Primitive.SByte => "sbyte",
			Primitive.Short => "short",
			Primitive.Int => "int",
			Primitive.UShort => "ushort",
			Primitive.UInt => "uint",
			Primitive.Float => "float",
			Primitive.Double => "double",
			Primitive.Long => "long",
			Primitive.ULong => "ulong",
			_ => throw new ArgumentException($"Unknown Primitive: {type}"),
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static uint BitLength(uint n) => (uint)(n == 0 ? 1 : 32 - LeadingZeroCount(n));
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string GetCast(Primitive type)
	{
		return $"({GetType(type)})";
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string GetPrimitiveDes(Primitive type, bool async = true) {
		return type switch
		{
			Primitive.String => $"{(async ? "await " : "")}%1.ReadString{(async ? "Async" : "")}()",
			Primitive.Bool => $"{(async ? "await " : "")}%1.ReadBool{(async ? "Async" : "")}()",
			Primitive.Byte => $"{(async ? "await " : "")}%1.ReadByte{(async ? "Async" : "")}()",
			Primitive.SByte => $"{(async ? "await " : "")}%1.ReadSByte{(async ? "Async" : "")}()",
			Primitive.Short => $"{(async ? "await " : "")}%1.ReadShort{(async ? "Async" : "")}()",
			Primitive.UShort => $"{(async ? "await " : "")}%1.ReadUShort{(async ? "Async" : "")}()",
			Primitive.Int => $"{(async ? "await " : "")}%1.ReadInt{(async ? "Async" : "")}()",
			Primitive.Long => $"{(async ? "await " : "")}%1.ReadLong{(async ? "Async" : "")}()",
			Primitive.Float => $"{(async ? "await " : "")}%1.ReadFloat{(async ? "Async" : "")}()",
			Primitive.UInt => $"{(async ? "await " : "")}%1.ReadUInt{(async ? "Async" : "")}()",
			Primitive.ULong => $"{(async ? "await " : "")}%1.ReadULong{(async ? "Async" : "")}()",
			Primitive.Double => $"{(async ? "await " : "")}%1.ReadDouble{(async ? "Async" : "")}()",
			_ => throw new ArgumentException($"Unknown PrimitiveType: {type}"),
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static string GetPrimitiveSer(Primitive type) {
		return "%1.Write(%2)";
	}

	static string GetStoreOpcodeForItem(FormatItem item) {
		return item switch
		{
			FormatPrimitive prim => prim.primitive switch
			{
				Primitive.Byte or Primitive.SByte or Primitive.Bool => "System.Reflection.Emit.OpCodes.Stind_I1",
				Primitive.Short or Primitive.UShort => "System.Reflection.Emit.OpCodes.Stind_I2",
				Primitive.Int or Primitive.UInt => "System.Reflection.Emit.OpCodes.Stind_I4",
				Primitive.Long or Primitive.ULong => "System.Reflection.Emit.OpCodes.Stind_I8",
				Primitive.Float => "System.Reflection.Emit.OpCodes.Stind_R4",
				Primitive.Double => "System.Reflection.Emit.OpCodes.Stind_R8",
				_ => "System.Reflection.Emit.OpCodes.Stind_Ref",
			},
			_ => "System.Reflection.Emit.OpCodes.Stind_Ref",
		};
	}

	static string GetLoadOpcodeForItem(FormatItem item) {
		return item switch
		{
			FormatPrimitive prim => prim.primitive switch
			{
				Primitive.SByte or Primitive.Bool => "System.Reflection.Emit.OpCodes.Ldind_I1",
				Primitive.Byte => "System.Reflection.Emit.OpCodes.Ldind_U1",
				Primitive.Short => "System.Reflection.Emit.OpCodes.Ldind_I2",
				Primitive.UShort => "System.Reflection.Emit.OpCodes.Ldind_U2",
				Primitive.Int => "System.Reflection.Emit.OpCodes.Ldind_U4",
				Primitive.UInt => "System.Reflection.Emit.OpCodes.Ldind_U4",
				Primitive.Long => "System.Reflection.Emit.OpCodes.Ldind_I8",
				Primitive.ULong => "System.Reflection.Emit.OpCodes.Ldind_U8",
				Primitive.Float => "System.Reflection.Emit.OpCodes.Ldind_R4",
				Primitive.Double => "System.Reflection.Emit.OpCodes.Ldind_R8",
				_ => "System.Reflection.Emit.OpCodes.Ldind_Ref",
			},
			_ => "System.Reflection.Emit.OpCodes.Ldind_Ref",
		};
	}

	static string GetType(FormatItem item) {
		return item switch
		{
			FormatStruct @struct => @struct.qualifiedName,
			FormatDictionary dict => dict.qualifiedName,
			FormatArray arr => $"{GetType(arr.element)}{(arr.elementNullable ? "?" : "")}[]",
			FormatEnum @enum => @enum.qualifiedName,
			FormatInterface @interface => @interface.qualifiedName,
			FormatPrimitive primitive => GetType(primitive.primitive),
			FormatQualifiedReference qual => qual.qualifiedName,
			_ => throw new Exception($"unsupported type {item}"),
		};
	}

	const string dm_name = "System.Reflection.Emit.DynamicMethod";

	const string binding_flags = "System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance";

	static string GetArgOpcode(int arg) {
		return arg switch
		{
			0 => "System.Reflection.Emit.OpCodes.Ldarg_0",
			1 => "System.Reflection.Emit.OpCodes.Ldarg_1",
			2 => "System.Reflection.Emit.OpCodes.Ldarg_2",
			3 => "System.Reflection.Emit.OpCodes.Ldarg_3",
			_ => arg <= 255 ? $"System.Reflection.Emit.OpCodes.Ldarg_S, {arg}" : $"System.Reflection.Emit.OpCodes.Ldarg, {arg}"
		};
	}

	static string GetArgAddrOpcode(int arg) {
		return arg <= 255 ? $"System.Reflection.Emit.OpCodes.Ldarga_S, {arg}" : $"System.Reflection.Emit.OpCodes.Ldarga, {arg}";
	}

	static bool IsUnmanaged(FormatItem item) {
		switch (item) {
			case FormatPrimitive prim:
				return prim.primitive switch
				{
					Primitive.String => false,
					_ => true,
				};
			case FormatStruct @struct:
				bool isUnmanaged = @struct.isUnmanaged;
				foreach (var field in @struct.fields) {
					if (!IsUnmanaged(field)) {
						isUnmanaged = false;
						break;
					}
				}
				return isUnmanaged;
			case FormatStructField field:
				return IsUnmanaged(field.item);
			default:
				return false;
		}
	}

	static SerdeReturn GetDes(GeneratorUtil.VariableTracker tracker, FormatItem item, bool async = false) {
		switch (item) {
			case FormatStruct @struct:
				if (@struct.isUnmanaged) {
					return (
						[$"{GeneratorUtil.Tabs()}%2 = {(async ? "await " : "")}%1.ReadUnmanaged{(async ? "Async" : "")}<{@struct.qualifiedName}>();"], 
						[],
						[]
					);
				}
				List<string> strings = [];
				List<FormatStructField> reflectionFields = [];
				List<string> classStrings = [];
				List<string> attrLines = [];
				List<FormatStructField> shadowStructFields = [];
				if (!@struct.record) {
					foreach (var field in @struct.fields) {
						if (field.@private) {
							reflectionFields.Add(field);
						}
						else {
							if (IsUnmanaged(field)) {
								shadowStructFields.Add(field);
							}
							else {
								(IEnumerable<string> func, _, _) = GetDes(tracker, field.item, async);
								if (field.nullable) {
									strings.Add($"if ({GetPrimitiveDes(Primitive.Bool, async)})");
								}
								strings.Add("{");
								foreach (string sfield in func) {
									string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
									strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
								}
								strings.Add("}");
							}
						}
					}
					if (shadowStructFields.Count <= 1) {
						foreach (var field in shadowStructFields) {
							(IEnumerable<string> func, _, _) = GetDes(tracker, field.item, async);
							if (field.nullable) {
								strings.Add($"if ({GetPrimitiveDes(Primitive.Bool, async)})");
							}
							strings.Add("{");
							foreach (string sfield in func) {
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
							}
							strings.Add("}");
						}
					}
					else if (!tracker.Exists("ShadowStruct")) {
						tracker.Add("ShadowStruct", typeof(void));
						classStrings.Add("public readonly struct Shadow {");
						foreach (var field in shadowStructFields) {
							classStrings.Add($"{GeneratorUtil.Tabs()}public readonly {GetFieldType(field)} {field.name};");
							// if (!field.nullable)
							// 	classStrings.Add($"{GeneratorUtil.Tabs()}public {GetType(field.item)} {field.name};");
							// else {
							// 	classStrings.Add($"{GeneratorUtil.Tabs()}public tairasoul.unity.common.util.UnmanagedNullable<{GetType(field.item)}> {field.name};");
							// }
						}
						string constructorStr = $"{GeneratorUtil.Tabs()}public Shadow(";
						var first = shadowStructFields.First();
						static string GetFieldType(FormatStructField field) {
							if (!field.nullable) {
								return GetType(field.item);
							}
							else {
								return $"tairasoul.unity.common.util.UnmanagedNullable<{GetType(field.item)}>";
							}
						}
						constructorStr += $"{GetFieldType(first)} {first.name}";
						foreach (var field in shadowStructFields.GetRange(1, shadowStructFields.Count - 1)) {
							constructorStr += $", {GetFieldType(field)} {field.name}";
						}
						constructorStr += ") {";
						classStrings.Add(constructorStr);
						foreach (var field in shadowStructFields) {
							classStrings.Add($"{GeneratorUtil.Tabs(2)}this.{field.name} = {field.name};");
						}
						classStrings.Add($"{GeneratorUtil.Tabs()}}}");
						classStrings.Add("}");
						attrLines.Add($"[HasShadowStruct(typeof({@struct.qualifiedName}))]");
						string shadowName = tracker.Generate(typeof(void));
						strings.Add($"Shadow {shadowName} = {(async ? "await " : "")}%1.ReadUnmanaged{(async ? "Async" : "")}<Shadow>();");
						foreach (var field in shadowStructFields) {
							strings.Add($"%2.{field.name} = {shadowName}.{field.name};");
						}
					}
					else {
						string shadowName = tracker.Generate(typeof(void));
						strings.Add($"Shadow {shadowName} = {(async ? "await " : "")}%1.ReadUnmanaged{(async ? "Async" : "")}<Shadow>();");
						foreach (var field in shadowStructFields) {
							strings.Add($"%2.{field.name} = {shadowName}.{field.name};");
						}
					}
					if (reflectionFields.Count > 0)
					{
						var split = @struct.qualifiedName.Split('.');
						var ns = GeneratorUtil.GetLastTwo(split);
						var refname = $"{string.Join("_", ns)}_PrivateVariable";
						var rfname = refname;
						List<(string type, string field, FormatItem item, string name, bool nullable)> types = [];
						List<string> rflines = [];
						foreach (var field in reflectionFields)
						{
							var fn = tracker.Generate(typeof(FormatStructField));
							(IEnumerable<string> func, _, _) = GetDes(tracker, field.item, async);
							var typename = GetType(field.item);
							types.Add((typename, field.name, field.item, fn, field.nullable));
							if (field.nullable)
								rflines.Add($"if ({GetPrimitiveDes(Primitive.Bool, async)})");
							rflines.Add("{");
							foreach (var line in func)
							{
								string end = !line.EndsWith("{") && !line.EndsWith("}") && !line.EndsWith(";") ? ";" : "";
								rflines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", fn)}{end}");
							}
							rflines.Add("}");
						}
						foreach (var type in types)
						{
							strings.Add($"{type.type}{(type.nullable ? "?" : "")} {type.name} {(type.nullable ? "= null" : "")};");
						}
						foreach (var inter in rflines)
						{
							strings.Add(inter);
						}
						string call = $"{rfname}Setter(ref %2, ";
						call += types[0].name;
						for (int i = 1; i < types.Count; i++) {
							call += ", ";
							call += types[i].name;
						}
						call += ");";
						strings.Add(call);
						// strings.Add($"return {types[0].name};");
						if (!tracker.Exists($"Set_{refname}"))
						{
							tracker.Add($"Set_{refname}", typeof(int));
							string structQualGen = tracker.Generate(typeof(void));
							string actionParams = "ref " + @struct.qualifiedName + " " + structQualGen;
							for (int i = 0; i < types.Count; i++) {
								actionParams += ", ";
								actionParams += types[i].type;
								actionParams += " ";
								actionParams += tracker.Generate(typeof(void));
							}
							classStrings.Add($"private delegate void {rfname}SetterDelegate({actionParams});");
							GeneratorUtil.VariableTracker scoped = new();
							var dmName = scoped.Generate(typeof(Delegate));
							var il = scoped.Generate(typeof(Delegate));
							classStrings.Add($"static {rfname}SetterDelegate {rfname}Setter = Create{rfname}Setter();");
							classStrings.Add($"static {rfname}SetterDelegate Create{rfname}Setter() {{");
							string methodTypes = "typeof(" + @struct.qualifiedName + ").MakeByRefType()";
							for (int i = 0; i < types.Count; i++) {
								methodTypes += ", typeof(";
								methodTypes += types[i].type;
								methodTypes += ")";
							}
							classStrings.Add($"{GeneratorUtil.Tabs()}{dm_name} {dmName} = new(\"SetValues\", typeof(void), [{methodTypes}], true);");
							classStrings.Add($"{GeneratorUtil.Tabs()}var {il} = {dmName}.GetILGenerator();");
							for (int i = 0; i < types.Count; i++) {
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit({GetArgAddrOpcode(0)});");
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit(System.Reflection.Emit.OpCodes.Ldind_Ref);");
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit({GetArgOpcode(i + 1)});");
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit(System.Reflection.Emit.OpCodes.Stfld, typeof({@struct.qualifiedName}).GetField(\"{types[i].field}\", {binding_flags}));");
							}
							classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit(System.Reflection.Emit.OpCodes.Ret);");
							classStrings.Add($"{GeneratorUtil.Tabs()}return ({rfname}SetterDelegate){dmName}.CreateDelegate(typeof({rfname}SetterDelegate));");
							classStrings.Add("}");
						}
					}
				}
				else {
					List<string> positionalFieldValues = [];
					foreach (var field in @struct.fields) {
						if (field.positional) {
							var pname = tracker.Generate(typeof(FormatStructField));
							positionalFieldValues.Add(pname);
							if (field.nullable)
								strings.Add($"{GeneratorUtil.Tabs()}{GetType(field.item)}? {pname} = null;");
							else
								strings.Add($"{GeneratorUtil.Tabs()}{GetType(field.item)} {pname};");
							if (field.nullable)
								strings.Add($"{GeneratorUtil.Tabs()}if ({GetPrimitiveDes(Primitive.Bool, async)})");
							strings.Add($"{GeneratorUtil.Tabs()}{{");
							(IEnumerable<string> pdes, _, _) = GetDes(tracker, field.item, async);
							foreach (var sfield in pdes) {
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								strings.Add($"{GeneratorUtil.Tabs(2)}{sfield.Replace("%2", pname)}{end}");
							}
							strings.Add($"{GeneratorUtil.Tabs()}}}");
						}
					}
					strings.Add($"{GeneratorUtil.Tabs()}%2 = new {@struct.qualifiedName}({string.Join(", ", positionalFieldValues)});");
				}
				return (strings, classStrings, attrLines);
			case FormatArray array:
				List<string> lines = [];
				string lengthName = tracker.Generate(typeof(int));
				string arrayName = tracker.Generate(typeof(FormatArray));
				string iterator = tracker.Generate(typeof(int));
				string name = tracker.Generate(typeof(FormatArray));
				lines.Add($"int {lengthName} = {GetPrimitiveDes(Primitive.Int, async)};");
				lines.Add($"{GetType(array)} {arrayName} = new {GetType(array)}[{lengthName}];");
				lines.Add($"for (int {iterator} = 0; {iterator} < {lengthName}; {iterator}++) {{");
				(IEnumerable<string> des, _, _) = GetDes(tracker, array.element, async);
				if (array.elementNullable) {
					lines.Add($"{GeneratorUtil.Tabs()}{GetType(array.element)}? {name} = null;");
					lines.Add($"{GeneratorUtil.Tabs()}if ({GetPrimitiveDes(Primitive.Bool, async)})");
					lines.Add($"{GeneratorUtil.Tabs()}{{");
					foreach (var line in des) {
						lines.Add($"{GeneratorUtil.Tabs(2)}{line.Replace("%2", name)}");
					}
					lines.Add($"{GeneratorUtil.Tabs()}}}");
					lines.Add($"{GeneratorUtil.Tabs()}{arrayName}[{iterator}] = {name};");
				}
				else {
					foreach (var line in des) {
						lines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{arrayName}[{iterator}];")}");
					}
				}
				lines.Add("}");
				lines.Add($"%2 = {arrayName}");
				return (lines, [], []);
			case FormatDictionary dict:
				List<string> dictlines = [];
				string lengthN = tracker.Generate(typeof(int));
				string dictName = tracker.Generate(typeof(int));
				string iteratorName = tracker.Generate(typeof(int));
				string keyName = tracker.Generate(typeof(int));
				string valueName = tracker.Generate(typeof(int));
				dictlines.Add($"int {lengthN} = {GetPrimitiveDes(Primitive.Int, async)};");
				dictlines.Add($"{dict.qualifiedName} {dictName} = [];");
				dictlines.Add($"for (int {iteratorName} = 0; {iteratorName} < {lengthN}; {iteratorName}++) {{");
				(IEnumerable<string> keyDes, _, _) = GetDes(tracker, dict.key, async);
				(IEnumerable<string> valueDes, _, _) = GetDes(tracker, dict.value, async);
				foreach (string line in keyDes) {
					dictlines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{GetType(dict.key)} {keyName}")}");
				}
				if (dict.valueNullable) {
					dictlines.Add($"{GeneratorUtil.Tabs()}{GetType(dict.value)}? {valueName} = null;");
					dictlines.Add($"{GeneratorUtil.Tabs()}if ({GetPrimitiveDes(Primitive.Bool, async)})");
					dictlines.Add($"{GeneratorUtil.Tabs()}{{");
					foreach (string line in valueDes) {
						dictlines.Add($"{GeneratorUtil.Tabs(2)}{line.Replace("%2", valueName)}");
					}
					dictlines.Add($"{GeneratorUtil.Tabs()}}}");
					dictlines.Add($"{GeneratorUtil.Tabs()}{dictName}[{keyName}] = {valueName};");
				}
				else {
					foreach (string line in valueDes) {
						dictlines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{dictName}[{keyName}]")}");
					}
				}
				dictlines.Add("}");
				dictlines.Add($"%2 = {dictName}");
				return (dictlines, [], []);
			case FormatEnum @enum:
				List<string> elines = [];
				if (@enum.compacted) {
					var bitlength = BitLength((uint)@enum.values.Length);
					string enumname = tracker.Generate(typeof(int));
					string varname = tracker.Generate(typeof(int));
					if (bitlength <= 8) {
						elines.Add($"byte {enumname} = {GetPrimitiveDes(Primitive.Byte, async)};");
					}
					else if (bitlength <= 16) {
						elines.Add($"short {enumname} = {GetPrimitiveDes(Primitive.Short, async)};");
					}
					else if (bitlength <= 32) {
						elines.Add($"int {enumname} = {GetPrimitiveDes(Primitive.Int, async)};");
					}
					else {
						elines.Add($"long {enumname} = {GetPrimitiveDes(Primitive.Long, async)};");
					}
					elines.Add($"{@enum.qualifiedName} {varname};");
					elines.Add($"switch ({enumname}) {{");
					for (int i = 0; i < @enum.values.Length; i++) {
						elines.Add($"{GeneratorUtil.Tabs()}case {i}:");
						elines.Add($"{GeneratorUtil.Tabs(2)}{varname} = {@enum.qualifiedName}.{@enum.values[i]};");
						elines.Add($"{GeneratorUtil.Tabs(2)}break;");
					}
					elines.Add($"{GeneratorUtil.Tabs()}default:");
					elines.Add($"{GeneratorUtil.Tabs(2)}throw new Exception(\"should be impossible (got compacted enum with invalid num)\")");
					elines.Add("}");
					elines.Add($"%2 = {varname}");
				}
				else {
					elines.Add($"%2 = ({@enum.qualifiedName}){GetPrimitiveDes(@enum.underlyingType, async)};");
				}
				return (
					elines,
					[],
					[]
				);
			case FormatPrimitive primitive:
				return (
					[$"%2 = {GetPrimitiveDes(primitive.primitive, async)}"],
					[],
					[]
				);
			case FormatQualifiedReference reference:
				return ([$"%2 = {(async ? "await " : "")}tairasoul.unity.common.format.items.formatters_{reference.qualifiedName}Format.Deserialize{(async ? "Async" : "")}(%1)"], [], []);
			default:
				return ([], [], []);
		}
	}

	static SerdeReturn GetSer(GeneratorUtil.VariableTracker tracker, FormatItem item) {
		switch (item) {
			case FormatStruct @struct:
				if (@struct.isUnmanaged) {
					return (
						[$"%1.WriteUnmanaged(%2);"], 
						[],
						[]
					);
				}
				List<string> strings = [];
				List<FormatStructField> reflectionFields = [];
				List<string> classStrings = [];
				List<string> attrLines = [];
				List<FormatStructField> shadowStructFields = [];
				if (!@struct.record) {
					foreach (var field in @struct.fields) {
						if (field.@private) {
							reflectionFields.Add(field);
						}
						else {
							if (IsUnmanaged(field)) {
								shadowStructFields.Add(field);
							}
							else
							{
								(IEnumerable<string> func, _, _) = GetSer(tracker, field.item);
								if (field.nullable)
								{
									if (IsUnmanaged(field)) {
										strings.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"%2.{field.name}.HasValue")};");
										strings.Add($"if (%2.{field.name}.HasValue)");
									}
									else {
										strings.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"%2.{field.name} != null")};");
										strings.Add($"if (%2.{field.name} != null)");
									}
								}
								strings.Add("{");
								foreach (string sfield in func)
								{
									string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
									if (IsUnmanaged(field) && field.nullable)
										strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}.Value")}{end}");
									else
										strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
								}
								strings.Add("}");
							}
						}
					}
					if (shadowStructFields.Count <= 1) {
						foreach (var field in shadowStructFields) {
							(IEnumerable<string> func, _, _) = GetSer(tracker, field.item);
							if (field.nullable) {
								strings.Add($"if ({GetPrimitiveSer(Primitive.Bool)})");
							}
							strings.Add("{");
							foreach (string sfield in func) {
								string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
								strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
							}
							strings.Add("}");
						}
					}
					else if (!tracker.Exists("ShadowStruct")) {
						tracker.Add("ShadowStruct", typeof(void));
						classStrings.Add("public readonly struct Shadow {");
						foreach (var field in shadowStructFields) {
							classStrings.Add($"{GeneratorUtil.Tabs()}public readonly {GetFieldType(field)} {field.name};");
						}
						string constructorStr = $"{GeneratorUtil.Tabs()}public Shadow(";
						static string GetFieldType(FormatStructField field) {
							if (!field.nullable) {
								return GetType(field.item);
							}
							else {
								return $"tairasoul.unity.common.util.UnmanagedNullable<{GetType(field.item)}>";
							}
						}
						var first = shadowStructFields.First();
						constructorStr += $"{GetFieldType(first)} {first.name}";
						foreach (var field in shadowStructFields.GetRange(1, shadowStructFields.Count - 1)) {
							constructorStr += $", {GetFieldType(field)} {field.name}";
						}
						constructorStr += ") {";
						classStrings.Add(constructorStr);
						foreach (var field in shadowStructFields) {
							classStrings.Add($"{GeneratorUtil.Tabs(2)}this.{field.name} = {field.name};");
						}
						classStrings.Add($"{GeneratorUtil.Tabs()}}}");
						classStrings.Add("}");
						classStrings.Add("static Type ShadowType = typeof(Shadow);");
						attrLines.Add($"[HasShadowStruct(typeof({@struct.qualifiedName}))]");
						string shadowName = tracker.Generate(typeof(void));
						string shadowCreation = $"Shadow {shadowName} = new Shadow(";
						var firstFiel = shadowStructFields.First();
						shadowCreation += $"%2.{firstFiel.name}";
						foreach (var field in shadowStructFields.GetRange(1, shadowStructFields.Count - 1)) {
							shadowCreation += $", %2.{field.name}";
						}
						shadowCreation += ");";
						strings.Add(shadowCreation);
						strings.Add($"%1.WriteUnmanaged({shadowName});");
					}
					else {
						string shadowName = tracker.Generate(typeof(void));
						// strings.Add($"Shadow {shadowName} = (Shadow)FormatterServices.GetUninitializedObject(ShadowType);");
						strings.Add($"tairasoul.unity.common.util.CustomUnsafe.SkipInit<Shadow>(out Shadow {shadowName})");
						foreach (var field in shadowStructFields) {
							strings.Add($"{shadowName}.{field.name} = %2.{field.name};");
						}
						strings.Add($"%1.WriteUnmanaged({shadowName});");
					}
					if (reflectionFields.Count > 0)
					{
						var split = @struct.qualifiedName.Split('.');
						var ns = GeneratorUtil.GetLastTwo(split);
						var refname = $"{string.Join("_", ns)}_PrivateVariable";
						var rfname = refname;
						List<(string type, string field, string name, FormatItem item, bool nullable)> types = [];
						List<string> rflines = [];
						foreach (var field in reflectionFields)
						{
							var fn = tracker.Generate(typeof(FormatStructField));
							(IEnumerable<string> func, _, _) = GetSer(tracker, field.item);
							var typename = GetType(field.item);
							types.Add((typename, field.name, fn, field.item, field.nullable));
							if (field.nullable)
							{
								if (IsUnmanaged(field)) {
									rflines.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"{fn}.HasValue")};");
									rflines.Add($"if ({fn}.HasValue)");
								}
								else {
									rflines.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"{fn} != null")};");
									rflines.Add($"if ({fn} != null)");
								}
							}
							rflines.Add("{");
							foreach (var line in func)
							{
								string end = !line.EndsWith("{") && !line.EndsWith("}") && !line.EndsWith(";") ? ";" : "";
								if (IsUnmanaged(field) && field.nullable)
									rflines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{fn}.Value")}{end}");
								else
									rflines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", fn)}{end}");
							}
							rflines.Add("}");
						}
						foreach (var type in types)
						{
							strings.Add($"{type.type}{(type.nullable ? "?" : "")} {type.name};");
						}
						string call = $"{rfname}Getter(ref %2, ";
						call += "out ";
						call += types[0].name;
						for (int i = 1; i < types.Count; i++)
						{
							call += ", out ";
							call += types[i].name;
						}
						call += ");";
						strings.Add(call);
						strings.AddRange(rflines);
						if (!tracker.Exists($"Get_{refname}"))
						{
							tracker.Add($"Get_{refname}", typeof(int));
							string structQualGen = tracker.Generate(typeof(void));
							string actionParams = "ref " + @struct.qualifiedName + " " + structQualGen;
							for (int i = 0; i < types.Count; i++)
							{
								actionParams += ", out ";
								actionParams += types[i].type;
								actionParams += " ";
								actionParams += tracker.Generate(typeof(void));
							}
							classStrings.Add($"private delegate void {rfname}GetterDelegate({actionParams});");
							GeneratorUtil.VariableTracker scoped = new();
							var dmName = scoped.Generate(typeof(Action));
							var il = scoped.Generate(typeof(Action));
							classStrings.Add($"static {rfname}GetterDelegate {rfname}Getter = Create{rfname}Getter();");
							classStrings.Add($"static {rfname}GetterDelegate Create{rfname}Getter() {{");
							string methodTypes = "typeof(" + @struct.qualifiedName + ").MakeByRefType()";
							for (int i = 0; i < types.Count; i++) {
								methodTypes += ", typeof(";
								methodTypes += types[i].type;
								methodTypes += ").MakeByRefType()";
							}
							classStrings.Add($"{GeneratorUtil.Tabs()}{dm_name} {dmName} = new(\"GetValues\", typeof(void), [{methodTypes}], true);");
							classStrings.Add($"{GeneratorUtil.Tabs()}var {il} = {dmName}.GetILGenerator();");
							for (int i = 0; i < types.Count; i++)
							{
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit({GetArgOpcode(i + 1)});");
								classStrings.Add($"{GeneratorUtil.Tabs(1)}{il}.Emit({GetArgOpcode(0)});");
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit(System.Reflection.Emit.OpCodes.Ldfld, typeof({@struct.qualifiedName}).GetField(\"{types[i].field}\", {binding_flags}));");
								classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit({GetStoreOpcodeForItem(types[i].item)});");
							}
							classStrings.Add($"{GeneratorUtil.Tabs()}{il}.Emit(System.Reflection.Emit.OpCodes.Ret);");
							classStrings.Add($"{GeneratorUtil.Tabs()}return ({rfname}GetterDelegate){dmName}.CreateDelegate(typeof({rfname}GetterDelegate));");
							classStrings.Add("}");
						}
					}
				}
				else {
					foreach (var field in @struct.fields) {
						if (field.@private) continue;
						if (field.nullable) {
							if (IsUnmanaged(field)) {
								strings.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"%2.{field.name}.HasValue")};");
								strings.Add($"if (%2.{field.name}.HasValue)");
							}
							else {
								strings.Add($"{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"%2.{field.name} != null")};");
								strings.Add($"if (%2.{field.name} != null)");
							}
						}
						strings.Add("{");
						(IEnumerable<string> fieldSer, _, _) = GetSer(tracker, field.item);
						foreach (string sfield in fieldSer) {
							string end = !sfield.EndsWith("{") && !sfield.EndsWith("}") && !sfield.EndsWith(";") ? ";" : "";
							if (IsUnmanaged(field) && field.nullable)
								strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}.Value")}{end}");
							else
								strings.Add($"{GeneratorUtil.Tabs()}{sfield.Replace("%2", $"%2.{field.name}")}{end}");
						}
						strings.Add("}");
					}
				}
				return (strings, classStrings, attrLines);
			case FormatArray array:
				List<string> arrlines = [];
				string elementName = tracker.Generate(typeof(int));
				arrlines.Add(GetPrimitiveSer(Primitive.Int).Replace("%2", "%2.Length"));
				arrlines.Add($"foreach (var {elementName} in %2) {{");
				(IEnumerable<string> arrser, _, _) = GetSer(tracker, array.element);
				if (array.elementNullable) {
					arrlines.Add($"{GeneratorUtil.Tabs()}{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"{elementName} != null")};");
					arrlines.Add($"{GeneratorUtil.Tabs()}if ({elementName} != null) {{");
					foreach (var str in arrser) {
						arrlines.Add($"{GeneratorUtil.Tabs(2)}{str.Replace("%2", elementName)}");
					}
					arrlines.Add($"{GeneratorUtil.Tabs()}}}");
				}
				else {
					foreach (var str in arrser) {
						arrlines.Add($"{GeneratorUtil.Tabs()}{str.Replace("%2", elementName)}");
					}
				}
				arrlines.Add("}");
				return (arrlines, [], []);
			case FormatDictionary dict:
				List<string> dictlines = [];
				string pairName = tracker.Generate(typeof(int));
				dictlines.Add(GetPrimitiveSer(Primitive.Int).Replace("%2", "%2.Count"));
				dictlines.Add($"foreach (var {pairName} in %2) {{");
				(IEnumerable<string> keySer, _, _) = GetSer(tracker, dict.key);
				(IEnumerable<string> valueSer, _, _) = GetSer(tracker, dict.value);
				foreach (string line in keySer) {
					dictlines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{pairName}.Key")}");
				}
				if (dict.valueNullable) {
					dictlines.Add($"{GeneratorUtil.Tabs()}{GetPrimitiveSer(Primitive.Bool).Replace("%2", $"{pairName}.Value != null")};");
					dictlines.Add($"{GeneratorUtil.Tabs()}if ({pairName}.Value != null) {{");
					foreach (string line in valueSer) {
						dictlines.Add($"{GeneratorUtil.Tabs(2)}{line.Replace("%2", $"{pairName}.Value")}");
					}
					dictlines.Add($"{GeneratorUtil.Tabs()}}}");
				}
				else {
					foreach (string line in valueSer) {
						dictlines.Add($"{GeneratorUtil.Tabs()}{line.Replace("%2", $"{pairName}.Value")}");
					}
				}
				dictlines.Add("}");
				return (dictlines, [], []);
			case FormatEnum @enum:
				List<string> elines = [];
				if (@enum.compacted) {
					var bitlength = BitLength((uint)@enum.values.Length);
					string ser;
					if (bitlength <= 8) {
						ser = GetPrimitiveSer(Primitive.Byte);
					}
					else if (bitlength <= 16) {
						ser = GetPrimitiveSer(Primitive.Short);
					}
					else if (bitlength <= 32) {
						ser = GetPrimitiveSer(Primitive.Int);
					}
					else {
						ser = GetPrimitiveSer(Primitive.Long);
					}
					elines.Add("switch (%2) {");
					for (int i = 0; i < @enum.values.Length; i++) {
						elines.Add($"{GeneratorUtil.Tabs(2)}case {@enum.qualifiedName}.{@enum.values[i]}:");
						elines.Add($"{GeneratorUtil.Tabs(2)}{ser.Replace("%2", i.ToString())};");
						elines.Add($"{GeneratorUtil.Tabs(2)}break;");
					}
					elines.Add("}");
				}
				else {
					elines.Add(GetPrimitiveSer(@enum.underlyingType));
				}
				return (elines, [], []);
			case FormatPrimitive primitive:
				return ([GetPrimitiveSer(primitive.primitive)], [], []);
			case FormatQualifiedReference reference:
				return ([$"tairasoul.unity.common.format.items.formatters_{reference.qualifiedName}Format.Serialize(%1, %2)"], [], []);
			default:
				return ([], [], []);
		}
	}

	const string GeneratedCodeData = "\"tairasoul.unity.common.sourcegen.format\", \"0.1.0\"";

	public static void Generate(SourceProductionContext prod, IEnumerable<FormatItem> items) {
		IEnumerable<FormatStruct> structs = [.. items.Select((v) => v is FormatStruct str ? str : null).Where(c => c is not null)!];
		foreach (FormatStruct @struct in structs) {
			IEnumerable<string> ns_pieces = @struct.qualifiedName.Split('.');
			string name = ns_pieces.Last();
			StringBuilder sb = new();
			sb.AppendLine("using System;");
			sb.AppendLine("using System.CodeDom.Compiler;");
			sb.AppendLine("using System.Reflection;");
			sb.AppendLine("using System.Threading.Tasks;");
			sb.AppendLine("using System.Runtime.Serialization;");
			sb.AppendLine("using System.Runtime.CompilerServices;");
			sb.AppendLine("using tairasoul.unity.common.format.attributes;");
			string[] ident = @struct.qualifiedName.Split('.');
			string last = ident.Last();
			IEnumerable<string> withoutLast = ident.Where((it) => it != last);
			string joined = string.Join(".", withoutLast);
			string add = !string.IsNullOrEmpty(joined) ? $"{joined}" : "";
			GeneratorUtil.VariableTracker scoped = new();
			(IEnumerable<string> ser, IEnumerable<string> ser_class, IEnumerable<string> ser_attr) = GetSer(scoped, @struct);
			(IEnumerable<string> deser_sync, IEnumerable<string> deser_class_sync, _) = GetDes(scoped, @struct);
			(IEnumerable<string> deser_async, IEnumerable<string> deser_class_async, _) = GetDes(scoped, @struct, true);
			sb.AppendLine($"namespace tairasoul.unity.common.format.items.formatters_{add};");
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"[FormatMethodsFor(typeof({@struct.qualifiedName}))]");
			foreach (var line in ser_attr)
				sb.AppendLine(line);
			sb.AppendLine($"{(@struct.@public ? "public " : "")}static class {name}Format {{");
			string typeName = scoped.Generate(typeof(Type));
			sb.AppendLine($"{GeneratorUtil.Tabs()}static Type {typeName} = typeof({@struct.qualifiedName});");
			scoped.Add("StaticType", typeName);
			{
				string structName = scoped.Generate(typeof(int));
				sb.AppendLine($"{GeneratorUtil.Tabs()}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
				sb.AppendLine($"{GeneratorUtil.Tabs()}public static void Serialize(FormatWriter writer, {@struct.qualifiedName} {structName}) {{");
				foreach (string s in ser) {
					sb.AppendLine($"{GeneratorUtil.Tabs(2)}{s.Replace("%1", "writer").Replace("%2", structName)}");
				}
				sb.AppendLine($"{GeneratorUtil.Tabs()}}}");
				foreach (string s in ser_class) {
					sb.AppendLine($"{GeneratorUtil.Tabs()}{s}");
				}
			}
			{
				string structName = scoped.Generate(typeof(int));
				sb.AppendLine($"{GeneratorUtil.Tabs()}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
				sb.AppendLine($"{GeneratorUtil.Tabs()}public static {@struct.qualifiedName} Deserialize(FormatReader reader) {{");
				if (!@struct.record && !@struct.isClass) {
					if (!@struct.isUnmanaged)
						deser_sync = [$"tairasoul.unity.common.util.CustomUnsafe.SkipInit<{@struct.qualifiedName}>(out {@struct.qualifiedName} {structName});" , .. deser_sync];
						// deser_sync = [$"{@struct.qualifiedName} {structName} = ({@struct.qualifiedName})System.Runtime.Serialization.FormatterServices.GetUninitializedObject({typeName});" , .. deser_sync];
						// deser_sync = [$"{@struct.qualifiedName} {structName} = tairasoul.unity.common.util.UninitializedFactory<{@struct.qualifiedName}>.Create();" , .. deser_sync];
				}
				else if (!@struct.record && @struct.isClass) {
					deser_sync = [$"{@struct.qualifiedName} {structName} = tairasoul.unity.common.util.UninitializedFactory<{@struct.qualifiedName}>.Create();" , .. deser_sync];
				}
				
				if (!deser_sync.Any((v) => v.Contains("%2 =")))
					deser_sync = [.. deser_sync, $"return {structName};"];
				foreach (string des in deser_sync) {
					sb.AppendLine($"{GeneratorUtil.Tabs(2)}{des.Replace("%1", "reader").Replace("%2 =", "return").Replace("%2", structName)}");
				}
				sb.AppendLine($"{GeneratorUtil.Tabs()}}}");
				foreach (string s in deser_class_sync) {
					sb.AppendLine($"{GeneratorUtil.Tabs()}{s}");
				}
			}
			{
				string structName = scoped.Generate(typeof(int));
				sb.AppendLine($"{GeneratorUtil.Tabs()}[MethodImpl(MethodImplOptions.AggressiveInlining)]");
				sb.AppendLine($"{GeneratorUtil.Tabs()}public static async Task<{@struct.qualifiedName}> DeserializeAsync(FormatReader reader) {{");
				if (!@struct.record && !@struct.isClass) {
					if (!@struct.isUnmanaged)
						deser_async = [$"tairasoul.unity.common.util.CustomUnsafe.SkipInit<{@struct.qualifiedName}>(out {@struct.qualifiedName} {structName});" , .. deser_async];
						// deser_async = [$"{@struct.qualifiedName} {structName} = ({@struct.qualifiedName})System.Runtime.Serialization.FormatterServices.GetUninitializedObject({typeName});" , .. deser_async];
						// deser_async = [$"{@struct.qualifiedName} {structName} = tairasoul.unity.common.util.UninitializedFactory<{@struct.qualifiedName}>.Create();" , .. deser_async];
				}
				else if (!@struct.record && @struct.isClass) {
					deser_async = [$"{@struct.qualifiedName} {structName} = tairasoul.unity.common.util.UninitializedFactory<{@struct.qualifiedName}>.Create();" , .. deser_async];
				}
				if (!deser_async.Any((v) => v.Contains("%2 =")))
					deser_async = [.. deser_async, $"return {structName};"];
				foreach (string des in deser_async) {
					sb.AppendLine($"{GeneratorUtil.Tabs(2)}{des.Replace("%1", "reader").Replace("%2 =", "return").Replace("%2", structName)}");
				}
				sb.AppendLine($"{GeneratorUtil.Tabs()}}}");
				foreach (string s in deser_class_async) {
					sb.AppendLine($"{GeneratorUtil.Tabs()}{s}");
				}
			}
			sb.AppendLine("}");
			prod.AddSource($"format/{@struct.qualifiedName.Replace(".", "_")}.g.cs", sb.ToString());
		}
	}
}