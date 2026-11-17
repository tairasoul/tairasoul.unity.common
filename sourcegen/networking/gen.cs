using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using tairasoul.unity.common.sourcegen.format;

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

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// while (!Debugger.IsAttached) {
		// 	Debugger.Launch();
		// }
		// Debugger.Break();

		var serdes = context.SyntaxProvider.CreateSyntaxProvider((node, Node_) => Generator.Predicate(node), (synt, ct) => Generator.Transform(synt.Node, synt.SemanticModel, ct));

		var correlatesSerdes = context.SyntaxProvider.ForAttributeWithMetadataName("tairasoul.unity.common.networking.attributes.packets.CorrelatesTo", (_, _) => true, (ctx, ct) =>
		{
			var attribute = ctx.Attributes.First();
			var syntax = (AttributeSyntax)attribute.ApplicationSyntaxReference!.GetSyntax();
			var firstArg = (TypeOfExpressionSyntax)syntax.ArgumentList!.Arguments.First().Expression;
			var synRef = firstArg.Type.GetReference();
			SemanticModel model = ctx.SemanticModel;
			var synNode = synRef.GetSyntax(ct);
			var items = Generator.TransformUnchecked(synNode, model, ct);
			return items;
		});

		var csC = correlatesSerdes.Collect();

		var sc = serdes.Collect();

		context.RegisterSourceOutput(sc.Combine(csC), (ctx, types) =>
		{
			try
			{
				List<FormatItem> serdesTypes = [];
				HashSet<string> encountered = [];
				var combined = types.Left.Concat(types.Right).SelectMany(v => v);
				foreach (FormatItem? type in combined)
				{
					if (type == null) continue;
					if (type is FormatStruct str)
					{
						if (encountered.Add(str.qualifiedName))
							serdesTypes.Add(str);
					}
					else
					{
						serdesTypes.Add(type);
					}
				}
				Generator.Generate(ctx, serdesTypes);
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