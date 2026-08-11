using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace tairasoul.unity.common.sourcegen.format;

[Generator]
public class FormatGen : IIncrementalGenerator
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
		var types = context.SyntaxProvider.CreateSyntaxProvider(
			predicate: (node, _) => Generator.Predicate(node),
			transform: (ctx, ct) => Generator.Transform(ctx.Node, ctx.SemanticModel, ct)
		);

		var collected = types.SelectMany((array, ct) => array).Where(c => c is not null).Collect();

		context.RegisterSourceOutput(collected, (ctx, types) =>
		{
			try {
				List<FormatItem> serdesTypes = [];
				HashSet<string> encountered = [];
				foreach (FormatItem type in types) {
					if (type is FormatStruct str) {
						if (encountered.Add(str.qualifiedName))
							serdesTypes.Add(str);
					}
					else {
						serdesTypes.Add(type!);
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