using System;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace tairasoul.unity.common.sourcegen.bits;

// sourcegen for generic serdes in bitreader & writer
// supports async and sync variants for both

[Generator]
public class BitSerDes : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var types = context.SyntaxProvider.CreateSyntaxProvider(
			predicate: (node, _) => SerdesGen.Predicate(node),
			transform: (ctx, ct) => SerdesGen.Transform(ctx.Node, ctx.SemanticModel, ct)
		);

		var optionsProvider = context.ParseOptionsProvider.Select((options, _) => options.PreprocessorSymbolNames.ToImmutableHashSet());

		var collected = types.SelectMany((array, ct) => array).Where(c => c is not null).Collect();
		context.RegisterSourceOutput(collected.Combine(optionsProvider), (ctx, types) =>
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
				SerdesGen.GenerateSerDes(ctx, serdesTypes, types.Right.Contains("BITWRITING_ASYNC_GENERICWRITE"), types.Right.Contains("BITREADING_ASYNC_GENERICREAD"));
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