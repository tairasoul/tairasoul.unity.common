using System.Reflection;
using tairasoul.unity.common.speedrunning.dsl.compiler;
using tairasoul.unity.common.speedrunning.dsl.config;
using tairasoul.unity.common.speedrunning.dsl.internals;
using tairasoul.unity.common.speedrunning.livesplit;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.runtime;

public record FileEntry(bool isDirectory, string name, string relativePath, IEnumerable<FileEntry> entries);

public static class RuntimeInterface {
	public static Livesplit livesplitInstance;
	static InternalDslOperations dslOp;
	static Compiler compiler = null;
	static Assembly current;
	static string? activeFile = null;
	internal static RuntimeBehaviour behaviour;

	static RuntimeInterface() {
		livesplitInstance = new();
		livesplitInstance.ConnectPipe();
		dslOp = new();
		dslOp.livesplit = livesplitInstance;
		GameObject go = new("SrDSLRuntimeInterface");
		GameObject.DontDestroyOnLoad(go);
		behaviour = go.AddComponent<RuntimeBehaviour>();
	}

	/// <summary>
	/// <para>
	/// Setup the runtime interface for the DSL.
	/// See DslCompilationConfig if you want to configure the class for extra method calls and the bounds registry.
	/// </para>
	/// </summary>
	/// <param name="modVersion">Version of the mod that implements runtime-specific components.</param>
	/// <param name="sourceDir">Source directory for srd files.</param>
	/// <param name="buildDir">Directory srd files are built to.</param>
	public static void Setup(string modVersion, string sourceDir, string buildDir) {
		compiler = new(buildDir, sourceDir, modVersion);
	}

	/// <summary>
	/// Gets all the available .srd files for use.
	/// </summary>
	/// <returns>An IEnumerable of file entries.</returns>
	/// <exception cref="InvalidOperationException"></exception>
	public static IEnumerable<FileEntry> GetAvailableFiles() {
		if (compiler == null) {
			throw new InvalidOperationException("Cannot get available files before setting up runtime interface.");
		}
		return readDirectory(compiler.sourcePath);
	}


	/// <summary>
	/// Gets the relative path of the currently active file.
	/// </summary>
	public static string? GetActiveFile() {
		return activeFile;
	}

	static IEnumerable<FileEntry> readDirectory(string path, string relative = "") {
		List<FileEntry> entries = [];
		foreach (var dir in Directory.GetDirectories(path)) {
			var et = readDirectory(dir, $"{Path.GetFileName(dir)}/");
			if (et.Count() == 0) continue;
			entries.Add(new(true, Path.GetFileName(dir), $"{relative}{Path.GetFileName(dir)}", et));
		}
		foreach (var file in Directory.GetFiles(path)) {
			if (!file.EndsWith(".srd")) continue;
			entries.Add(new(false, Path.GetFileName(file), $"{relative}{Path.GetFileName(file)}", []));
		}
		return entries;
	}

	/*/// <summary>
	/// Unload the currently loaded file.
	/// </summary>
	public static void Unload() {
		behaviour.activeFile = null;
		behaviour.IsActive = false;
		AppDomain.CurrentDomain.Unl
	}*/

	/// <summary>
	/// Load a file entry and set it as the active file.
	/// </summary>
	/// <param name="file">FileEntry from GetAvailableFiles()</param>
	public static void Load(FileEntry file) {
		Load(file.relativePath);
	}

	/// <summary>
	/// Load a relative file path and set it as the active file.
	/// </summary>
	/// <param name="file">Path relative to sourceDir.</param>
	public static void Load(string file) {
		if (DslCompilationConfig.BoundsRegistryClass == null) {
			throw new InvalidOperationException("Tried to compile or load a split file without setting DslCompilationConfig.BoundsRegistryClass.");
		}
		activeFile = file;
		AccessorUtil.ClearCache();
		Assembly assembly = compiler.Compile(file);
		current = assembly;
		Type sfType = assembly.GetType("SplitFile");
		object sfInst = Activator.CreateInstance(sfType, dslOp, DslCompilationConfig.BoundsRegistryClass);;
		behaviour.activeFile = new(sfInst);
	}

	/// <summary>
	/// Call when splits should start running.
	/// </summary>
	public static void GameStarted() {
		behaviour.IsActive = true;
	}
}