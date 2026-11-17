using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using tairasoul.unity.common.repacker;

// while (!Debugger.IsAttached) {
// 	Debugger.Launch();
// }

string outputPath = args[0];
string csprojPath = args[1];

var doc = XDocument.Load(File.OpenRead(csprojPath));

var repackingConfig = doc.Descendants("RepackConfig").FirstOrDefault();
if (repackingConfig == null) return;
var fullDep = repackingConfig.Descendants("DependencyMode").FirstOrDefault();
var moduleInit = repackingConfig.Descendants("RepackModuleIniit").FirstOrDefault();
var treatOutput = repackingConfig.Descendants("OutputIsRelease").FirstOrDefault();
var targetsElem = repackingConfig.Descendants("Targets").FirstOrDefault();
var privatize = repackingConfig.Descendants("Privatize").FirstOrDefault();
var repackDebug = repackingConfig.Descendants("RepackPdb").FirstOrDefault();
if (targetsElem == null) return;
RepackMode repackMode = RepackMode.Full;
bool repackInit = true;
bool outputRelease = true;
bool privatizeDeps = false;
DebugRepack repackPdb = DebugRepack.Portable;
if (fullDep != null) {
	var v = fullDep.Value.Trim().ToLower();
	if (v == "full") {
		repackMode = RepackMode.Full;
	}
	else if (v == "strict") {
		repackMode = RepackMode.Strict;
	}
	else {
		repackMode = RepackMode.Referenced;
	}
}
if (moduleInit != null) {
	repackInit = moduleInit.Value.Trim().ToLower() == "true";
}
if (treatOutput != null) {
	outputRelease = treatOutput.Value.Trim().ToLower() == "true";
}
if (privatize != null) {
	privatizeDeps = privatize.Value.Trim().ToLower() == "true";
}
if (repackDebug != null) {
	var v = repackDebug.Value.Trim().ToLower();
	if (v == "embed") {
		repackPdb = DebugRepack.Embedded;
	}
	else if (v == "portable") {
		repackPdb = DebugRepack.Portable;
	}
	else if (v == "ignore") {
		repackPdb = DebugRepack.Ignore;
	}
	else {
		repackPdb = DebugRepack.None;
	}
}
List<string> targets = [];
foreach (var split in targetsElem.Value.Trim().Split(";")) {
	targets.Add(split.Trim());
}
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(outputPath));
var stream = File.OpenRead(outputPath);
bool symRead = repackPdb != DebugRepack.None && repackPdb != DebugRepack.Ignore;
var asm = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters() { AssemblyResolver = resolver, ReadSymbols = symRead, SymbolReaderProvider = new DefaultSymbolReaderProvider(false) });
var deps = DependencyMapper.ResolveDependencies(asm, resolver, symRead);
var mapping = DependencyMapper.Map([asm], deps, (d) => targets.Contains(d.Name.Name));
RepackingOptions options = new(privatizeDeps ? Privatize : null, repackMode, repackInit, symRead, outputRelease);
AssemblyDefinition newAsm = AssemblyDefinition.CreateAssembly(asm.Name, asm.MainModule.Name, ModuleKind.Dll);
Repacker repacker = new(mapping, newAsm.MainModule, options);
repacker.Target(asm.MainModule);
repacker.Run();
asm.Dispose();
foreach (var kv in deps) {
	foreach (var v in kv.Value) v.Dispose();
}
stream.Dispose();
var writer = new WriterParameters();
var pdb = Path.ChangeExtension(outputPath, "pdb");
if (repackPdb != DebugRepack.Ignore && File.Exists(pdb))
{
	File.Delete(pdb);
	if (symRead)
	{
		writer.WriteSymbols = true;
		if (repackPdb == DebugRepack.Embedded)
		{
			writer.SymbolWriterProvider = new EmbeddedPortablePdbWriterProvider();
		}
		else
		{
			writer.SymbolStream = File.OpenWrite(pdb);
			writer.SymbolWriterProvider = new PortablePdbWriterProvider();
		}
	}
}
newAsm.Write(File.OpenWrite(outputPath), writer);

void Privatize(TypeDefinition original, TypeDefinition copy) {
	if (original.Module.Assembly != asm) {
		copy.IsPublic = false;
	}
}

enum DebugRepack {
	Embedded,
	Portable,
	Ignore,
	None
}