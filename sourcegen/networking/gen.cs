using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using sdi = System.Collections.Immutable.ImmutableArray<tairasoul.unity.common.sourcegen.networking.SymbolData>;

// sourcegen for networking layer & as a consequence also for the format serdes
// automatically creates client & server deserialization steps for specific packet types
// networked aspects use sync writer and async reader

namespace tairasoul.unity.common.sourcegen.networking;

[Generator]
public class NetworkGen : IIncrementalGenerator
{
	internal static readonly SymbolDisplayFormat format = new(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
	);

	static string GetInternalCorrelation(sdi types, string correl) {
		foreach (var ic in types) {
			if (ic.arguments.First().EndsWith(correl))
				return ic.qualName;
		}
		return "";
	}

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// while (!Debugger.IsAttached) {
		// 	Debugger.Launch();
		// }
		// Debugger.Break();

		var correlates = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.CorrelatesTo", (_, _) => true, (ctx, _) => {
			IFieldSymbol symb = (IFieldSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.CorrelatesTo, symb.Name, symb.ToDisplayString(format), symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ((List<string>)[((AttributeSyntax)ctx.Attributes.First().ApplicationSyntaxReference!.GetSyntax()).ArgumentList!.Arguments.First().ToString()]).ToImmutableArray());
		}).Where(c => c is not null);

		var icorrelates = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.CorrelatesToInternal", (_, _) => true, (ctx, _) => {
			IFieldSymbol symb = (IFieldSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.CorrelatesToInternal, $"{symb.ContainingSymbol.Name}.{symb.Name}", symb.ToDisplayString(format).Replace($".{symb.Name}", ""), symb.ToDisplayString(format), ((List<string>)[((AttributeSyntax)ctx.Attributes.First().ApplicationSyntaxReference!.GetSyntax()).ArgumentList!.Arguments.First().ToString()]).ToImmutableArray());
		}).Where(c => c is not null);

		var relay = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ServerRelay", (_, _) => true, (ctx, _) =>
		{
			IFieldSymbol symb = (IFieldSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ServerRelay, $"{symb.ContainingSymbol.Name}.{symb.Name}", symb.ToDisplayString(format).Replace($".{symb.Name}", ""), symb.ToDisplayString(format), ((List<string>)[((AttributeSyntax)ctx.Attributes.First().ApplicationSyntaxReference!.GetSyntax()).ArgumentList!.Arguments.First().ToString()]).ToImmutableArray());
		}).Where(c => c is not null);

		var reliability = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.Reliability", (_, _) => true, (ctx, _) =>
		{
			IFieldSymbol symb = (IFieldSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.Reliability, $"{symb.ContainingSymbol.Name}.{symb.Name}", symb.ToDisplayString(format), symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ((List<string>)[.. ctx.Attributes.Select(v => ((int)v.ConstructorArguments.First().Value!) == 1 ? "Unreliable" : "Reliable")]).ToImmutableArray());
		}).Where(c => c is not null);

		var packetident = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.PacketTypeIdentifier", (_, _) => true, (ctx, _) => {
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.PacketTypeIdentifier, symb.Name, symb.ToDisplayString(format), symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty);
		}).Where(c => c is not null);

		var correlatesC = correlates.Collect();
		var icorrelatesC = icorrelates.Collect();
		var relayC = relay.Collect();
		var reliabilityC = reliability.Collect();
		var packetidentC = packetident.Collect();
		context.RegisterSourceOutput(correlatesC.Combine(icorrelatesC).Combine(relayC).Combine(reliabilityC).Combine(packetidentC).Select((t, _) => new Types(
			t.Left.Left.Left.Left,
			t.Left.Left.Left.Right,
			t.Left.Left.Right,
			t.Left.Right,
			t.Right
		)), (ctx, types) =>
		{
			try {
				NetGen.Codegen(ctx, types);
			}
			catch (Exception ex)
			{
				ctx.ReportDiagnostic(Diagnostic.Create(
					new DiagnosticDescriptor(
						"GEN001",
						"Source generator exception",
						$"Exception: {ex.ToString().Replace("\r\n", " | ").Replace("\n", " | ")}",
						"Generation",
						DiagnosticSeverity.Error,
						true),
					Location.None));
			}
		});

		var serdes = context.SyntaxProvider.CreateSyntaxProvider((node, Node_) => FormatGen.Predicate(node), (synt, ct) => FormatGen.Transform(synt.Node, synt.SemanticModel, ct));

		var correlatesSerdes = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.CorrelatesTo", (_, _) => true, (ctx, ct) =>
		{
			// IFieldSymbol symbol = (IFieldSymbol)ctx.TargetSymbol;
			var attribute = ctx.Attributes.First();
			var syntax = (AttributeSyntax)attribute.ApplicationSyntaxReference!.GetSyntax();
			var firstArg = (TypeOfExpressionSyntax)syntax.ArgumentList!.Arguments.First().Expression;
			var synRef = firstArg.Type.GetReference();
			SemanticModel model = ctx.SemanticModel;
			var synNode = synRef.GetSyntax(ct);
			return FormatGen.TransformUnchecked(synNode, model, ct);
			// firstArg.
			// var attributes = symbol.GetAttributes();
			// foreach (var attr in attributes) {
			// 	if (attr.AttributeClass != null && attr.AttributeClass.MetadataName == "tairasoul.unity.common.networking.attributes.packets.CorrelatesTo") {
			// 		var type = attr.ConstructorArguments.First();
			// 		var value = (Type)type.Value!;

			// 	}
			// }
		});

		var csC = correlatesSerdes.SelectMany((array, ct) => array).Where(c => c is not null).Collect();

		var sc = serdes.SelectMany((array, ct) => array).Where(c => c is not null).Collect();

		context.RegisterSourceOutput(sc.Combine(csC).Combine(icorrelatesC), (ctx, types) =>
		{
			try {
				List<FormatItem> serdesTypes = [];
				HashSet<string> encountered = [];
				FormatItem[] combined = [.. types.Left.Left!, .. types.Left.Right!];
				foreach (FormatItem type in combined) {
					if (type is FormatStruct str) {
						if (encountered.Add(str.qualifiedName))
							serdesTypes.Add(str);
					}
					else {
						serdesTypes.Add(type!);
					}
				}
				if (GetInternalCorrelation(types.Right, "Connect") != "") {
					FormatStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalConnectPacket", "tairasoul.unity.common.networking.gentypes", ((List<FormatStructField>)[
						new("udpPort", new FormatPrimitive(Primitive.Int), false, true, false),
						new("username", new FormatPrimitive(Primitive.String), false, true, false)
					]).ToImmutableArray(), true, false, false, false);
					serdesTypes.Add(typeStruct);
				}
				if (GetInternalCorrelation(types.Right, "IdRelay") != "") {
					FormatStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalIdRelayPacket", "tairasoul.unity.common.networking.gentypes", ((List<FormatStructField>)[
						new("playerId", new FormatPrimitive(Primitive.UShort), false, true, false)
					]).ToImmutableArray(), true, false, false, false);
					serdesTypes.Add(typeStruct);
				}
				if (GetInternalCorrelation(types.Right, "PlayerConnected") != "") {
					FormatStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalPlayerConnectedPacket", "tairasoul.unity.common.networking.gentypes", ((List<FormatStructField>)[
						new("playerId", new FormatPrimitive(Primitive.UShort), false, true, false),
						new("username", new FormatPrimitive(Primitive.String), false, true, false)
					]).ToImmutableArray(), true, false, false, false);
					serdesTypes.Add(typeStruct);
				}
				FormatGen.Generate(ctx, serdesTypes);
			}
			catch (Exception ex)
			{
				ctx.ReportDiagnostic(Diagnostic.Create(
					new DiagnosticDescriptor(
						"GEN001",
						"Source generator exception",
						$"Exception: {ex.ToString().Replace("\r\n", " | ").Replace("\n", " | ")}",
						"Generation",
						DiagnosticSeverity.Error,
						true),
					Location.None));
			}
		});
	}
}