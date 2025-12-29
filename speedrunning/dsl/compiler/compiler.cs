using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Antlr4.Runtime;
using tairasoul.unity.common.hashing;

namespace tairasoul.unity.common.speedrunning.dsl.compiler;

class Compiler {
	internal string buildPath;
	internal string sourcePath;
	static string mvHash = null;

	public Compiler(string build, string source, string modVersion) {
		buildPath = build;
		sourcePath = source;
		if (!Directory.Exists(build))
			Directory.CreateDirectory(build);
		if (!Directory.Exists(source))
			Directory.CreateDirectory(source);
		mvHash ??= Murmur3.Hash128(modVersion);
	}

	bool RequiresRecompilation(string file) {
		string hashPath = Path.Combine(buildPath, Path.GetFileNameWithoutExtension(file) + ".murmur3");
		if (!File.Exists(hashPath) || !File.Exists(Path.Combine(buildPath, Path.GetFileNameWithoutExtension(file) + ".dll"))) return true;
		string filePath = Path.Combine(sourcePath, file);
		string filehash = Murmur3.Hash128(File.ReadAllText(filePath));
		string existingHash = File.ReadAllText(hashPath);
		return (filehash + mvHash) != existingHash;
	}

	void WriteHash(string file) {
		string hashPath = Path.Combine(buildPath, Path.GetFileNameWithoutExtension(file) + ".murmur3");
		string filePath = Path.Combine(sourcePath, file);
		string filehash = Murmur3.Hash128(File.ReadAllText(filePath));
		File.WriteAllText(hashPath, filehash + mvHash);
	}

	public Assembly Compile(string file) {
		if (RequiresRecompilation(file)) {
			SrDslLexer lexer = new(CharStreams.fromPath(Path.Combine(sourcePath, file)));
			SrDslParser parser = new(new CommonTokenStream(lexer));
			SrDslParser.RootContext root = parser.root();
			TranslationVisitor visitor = new();
			AssemblyBuilder builder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName($"{file}.CompiledSplits"), AssemblyBuilderAccess.RunAndSave, buildPath);
			ModuleBuilder module = builder.DefineDynamicModule("compiled", Path.GetFileNameWithoutExtension(file) + ".dll");
			TranslationRootNode result = (TranslationRootNode)root.Accept(visitor);
			CompilationVisitor compilation = new(module, result.order.splits);
			foreach (var split in result.results)
				compilation.Visit((TranslationSplit)split);
			compilation.Finish();
			if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(file)))
				Directory.CreateDirectory(Path.GetDirectoryName(file));
			builder.Save(Path.GetFileNameWithoutExtension(file) + ".dll");
			WriteHash(file);
			return builder;
		}
		return LoadCompiled(file);
	}

	Assembly LoadCompiled(string file) {
		string compiledPath = Path.Combine(buildPath, Path.GetFileNameWithoutExtension(file) + ".dll");
		return Assembly.LoadFile(compiledPath);
	}
}