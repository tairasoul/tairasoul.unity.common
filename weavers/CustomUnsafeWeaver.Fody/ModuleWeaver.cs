using System.Diagnostics;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;

public class ModuleWeaver : BaseModuleWeaver
{
	public override void Execute()
	{
		TypeDefinition type = ModuleDefinition.GetType("tairasoul.unity.common.util.CustomUnsafe");
		MakeSkipInit(type);
	}

	void MakeSkipInit(TypeDefinition definition) {
		MethodDefinition md = definition.Methods.First((v) => v.Name == "SkipInit");
		MethodBody body = md.Body;
		ILProcessor processor = body.GetILProcessor();
		processor.Body.Instructions.Clear();
		processor.Emit(OpCodes.Ret);
		body.MaxStackSize = 8;
	}

	public override IEnumerable<string> GetAssembliesForScanning()
	{
		return [];
	}
}