using Mono.Cecil;
using Mono.Cecil.Cil;

static class TypeWeaves {
	public static void UnsafeSkipInit(TypeDefinition type) {
		MethodDefinition md = type.Methods.First((v) => v.Name == "SkipInit");
		MethodBody body = md.Body;
		ILProcessor processor = body.GetILProcessor();
		processor.Body.Instructions.Clear();
		processor.Emit(OpCodes.Ret);
		body.MaxStackSize = 8;
	}
}