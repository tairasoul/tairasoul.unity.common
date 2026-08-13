using System.Diagnostics;
using Mono.Cecil;

string path = args[0];

ReaderParameters parameters = new(ReadingMode.Immediate)
{
	InMemory = true,
	ReadWrite = true,
};

AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path, parameters);
ModuleDefinition module = assembly.MainModule;

TypeDefinition CustomUnsafe = module.GetType("tairasoul.unity.common.util.CustomUnsafe").Resolve();

TypeWeaves.UnsafeSkipInit(CustomUnsafe);

assembly.Write(path);
assembly.Dispose();