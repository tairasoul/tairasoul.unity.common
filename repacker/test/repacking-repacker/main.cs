using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using tairasoul.unity.common.repacker;

// while (!Debugger.IsAttached) {
// 	Debugger.Launch();
// }

string[] exclude = ["System.Collections.dll", "System.Console.dll", "System.IO.Compression.dll", "System.Linq.dll", "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Runtime.Serialization.Formatters.dll", "System.Security.Cryptography.dll", "System.Threading.dll"];

var stopwatch = new Stopwatch();
Directory.SetCurrentDirectory(Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net10.0"));
var cd = Directory.GetCurrentDirectory();
var resolver = new DefaultAssemblyResolver();
FieldInfo cacheField = typeof(DefaultAssemblyResolver).GetField("cache", BindingFlags.NonPublic | BindingFlags.Instance);
var cache = (IDictionary<string, AssemblyDefinition>)cacheField.GetValue(resolver);
Console.WriteLine(cache.Count);
var test = AssemblyDefinition.ReadAssembly("test.dll", new ReaderParameters() { AssemblyResolver = resolver, SymbolReaderProvider = new DefaultSymbolReaderProvider(false), ReadSymbols = true });
Console.WriteLine(cache.Count);
stopwatch.Start();
var deps = DependencyMapper.ResolveDependencies(test, resolver, true);
stopwatch.Stop();
Console.WriteLine($"took {stopwatch.Elapsed} to resolve dependencies of {test}");
stopwatch.Reset();
stopwatch.Start();
var mapped = DependencyMapper.Map([test], deps, (d) => !exclude.Contains(d.Name.Name + ".dll") && Path.Exists(Path.Combine(cd, d.Name.Name + ".dll")));
Console.WriteLine(cache.Count);
Console.WriteLine($"took {stopwatch.Elapsed} to map dependencies of {test}");
stopwatch.Stop();
stopwatch.Reset();
AssemblyDefinition newAsm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("repacked", new Version()), "module", ModuleKind.Dll);
Repacker repacker = new(mapped, newAsm.MainModule, new(null, RepackMode.Full, true, true, true));
repacker.Target(test.MainModule.GetType("Program"));
stopwatch.Start();
repacker.Run();
stopwatch.Stop();
Console.WriteLine($"took {stopwatch.Elapsed} to repack {newAsm.MainModule.GetTypes().Count()} types");
Console.WriteLine(cache.Count);
try {
	WriterParameters p = new();
	p.WriteSymbols = true;
	var path = Path.Combine(cd, "merged.dll");
	var pdb = Path.ChangeExtension(path, "pdb")!;
	// var provider = new PortablePdbWriterProvider();
	// p.SymbolStream = new FileStream(pdb, FileMode.Create);
	var provider = new EmbeddedPortablePdbWriterProvider();
	p.SymbolWriterProvider = provider;
	newAsm.Write(path, p);
}
catch (Exception ex) {
	Console.WriteLine($"exception writing asm {ex}");
}