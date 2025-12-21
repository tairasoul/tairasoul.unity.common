using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using sdi = System.Collections.Immutable.ImmutableArray<tairasoul.unity.common.sourcegen.networking.SymbolData>;

// sourcegen for networking layer & as a consequence also for the bit serdes
// automatically creates client & server deserialization steps for specific packet types
// only supports synchronous writer & asynchronous writer here, main bit serdes sourcegen supports both

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
		var correlates = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.CorrelatesTo", (_, _) => true, (ctx, _) => {
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
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

		var unreliableread = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementUnreliableRead", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementUnreliableRead, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty, symb.AllInterfaces.Any(i => i.ToDisplayString(format) == "tairasoul.unity.common.networking.clients.IClient"));
		});

		var reliableread = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementReliableRead", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementReliableRead, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty, symb.AllInterfaces.Any(i => i.ToDisplayString(format) == "tairasoul.unity.common.networking.clients.IClient"));
		});

		var unreliableheaderwrite = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementUnreliableHeaderWrite", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementUnreliableHeaderWrite, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty, symb.AllInterfaces.Any(i => i.ToDisplayString(format) == "tairasoul.unity.common.networking.clients.IClient"));
		});

		var reliableheaderwrite = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementReliableHeaderWrite", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementReliableHeaderWrite, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty, symb.AllInterfaces.Any(i => i.ToDisplayString(format) == "tairasoul.unity.common.networking.clients.IClient"));
		});

		var reliabilityget = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementReliabilityGet", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementReliabilityGet, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty);
		});

		var serverrelay = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.ImplementServerRelay", (_, _) => true, (ctx, _) =>
		{
			INamedTypeSymbol symb = (INamedTypeSymbol)ctx.TargetSymbol;
			return new SymbolData(AttributeUsed.ImplementServerRelay, symb.Name, symb.Name, symb.ToDisplayString(format).Replace($".{symb.Name}", ""), ImmutableArray<string>.Empty);
		});

		var correlatesC = correlates.Collect();
		var icorrelatesC = icorrelates.Collect();
		var relayC = relay.Collect();
		var reliabilityC = reliability.Collect();
		var packetidentC = packetident.Collect();
		var unreliablereadC = unreliableread.Collect();
		var reliablereadC = reliableread.Collect();
		var unreliableheaderC = unreliableheaderwrite.Collect();
		var reliableheaderC = reliableheaderwrite.Collect();
		var rgC = reliabilityget.Collect();
		var srC = serverrelay.Collect();
		context.RegisterSourceOutput(correlatesC.Combine(icorrelatesC).Combine(relayC).Combine(reliabilityC).Combine(packetidentC).Combine(unreliablereadC).Combine(reliablereadC).Combine(unreliableheaderC).Combine(reliableheaderC).Combine(rgC).Combine(srC).Select((t, _) => new Types(
			t.Left.Left.Left.Left.Left.Left.Left.Left.Left.Left,
			t.Left.Left.Left.Left.Left.Left.Left.Left.Left.Right,
			t.Left.Left.Left.Left.Left.Left.Left.Left.Right,
			t.Left.Left.Left.Left.Left.Left.Left.Right,
			t.Left.Left.Left.Left.Left.Left.Right,
			t.Left.Left.Left.Left.Left.Right,
			t.Left.Left.Left.Left.Right,
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

		var serdes = context.SyntaxProvider.CreateSyntaxProvider((node, Node_) => SerdesGen.Predicate(node), (synt, ct) => SerdesGen.Transform(synt.Node, synt.SemanticModel, ct));

		var sc = serdes.SelectMany((array, ct) => array).Where(c => c is not null).Collect();

		context.RegisterSourceOutput(sc.Combine(icorrelatesC), (ctx, types) =>
		{
			try {
				List<SerdesType> serdesTypes = [];
				HashSet<string> encountered = [];
				foreach (SerdesType type in types.Left!) {
					if (type is SerdesTypeStruct str) {
						if (encountered.Add(str.qualifiedName))
							serdesTypes.Add(str);
					}
					else {
						serdesTypes.Add(type!);
					}
				}
				if (GetInternalCorrelation(types.Right, "Connect") != "") {
					SerdesTypeStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalConnectPacket", ((List<SerdesTypeStructField>)[
						new("udpPort", new SerdesTypePrimitive(PrimitiveType.Int), isPositional: true),
						new("username", new SerdesTypePrimitive(PrimitiveType.String), isPositional: true)
					]).ToImmutableArray(), true);
					serdesTypes.Add(typeStruct);
				}
				if (GetInternalCorrelation(types.Right, "IdRelay") != "") {
					SerdesTypeStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalIdRelayPacket", ((List<SerdesTypeStructField>)[
						new("playerId", new SerdesTypePrimitive(PrimitiveType.UShort), size: 12, isPositional: true)
					]).ToImmutableArray(), true);
					serdesTypes.Add(typeStruct);
				}
				if (GetInternalCorrelation(types.Right, "PlayerConnected") != "") {
					SerdesTypeStruct typeStruct = new("tairasoul.unity.common.networking.gentypes.InternalPlayerConnectedPacket", ((List<SerdesTypeStructField>)[
						new("playerId", new SerdesTypePrimitive(PrimitiveType.UShort), size: 12, isPositional: true),
						new("username", new SerdesTypePrimitive(PrimitiveType.String), isPositional: true)
					]).ToImmutableArray(), true);
					serdesTypes.Add(typeStruct);
				}
				SerdesGen.GenerateSerDes(ctx, serdesTypes);
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