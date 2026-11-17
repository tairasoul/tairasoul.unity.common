using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using tairasoul.unity.common.repacker;
// using tairasoul.unity.common.repacker.old;

// while (!Debugger.IsAttached) {
// 	Debugger.Launch();
// }

Directory.SetCurrentDirectory(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net10.0"));
var cd = Directory.GetCurrentDirectory();
var resolver = new DefaultAssemblyResolver();
Console.WriteLine("reading asm");
var asm4 = AssemblyDefinition.ReadAssembly("asm4.dll", new ReaderParameters() { AssemblyResolver = resolver });
Console.WriteLine("read asm");
var deps = DependencyMapper.ResolveDependencies(asm4, resolver);
var mapped = DependencyMapper.Map([asm4], deps, (d) => Path.Exists(Path.Combine(cd, d.Name.Name + ".dll")));
AssemblyDefinition newAsm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("repacked", new Version()), "module", ModuleKind.Dll);
Repacker repacker = new(mapped, newAsm.MainModule, new(null, RepackMode.Referenced, true, false));
repacker.Target(asm4.MainModule.GetType("D`1"));
var stopwatch = new Stopwatch();
stopwatch.Start();
repacker.Run();
stopwatch.Stop();
Console.WriteLine($"took {stopwatch.Elapsed} to repack {newAsm.MainModule.GetTypes().Count()} types");
var p = Path.Combine(cd, "merged-test.dll");
Console.WriteLine(p);
Console.WriteLine("writing to disk");
try {
	newAsm.Write(p);
}
catch (Exception ex) {
	Console.WriteLine($"exception writing asm {ex}");
}