using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace tairasoul.unity.common.repacker;

public record AssemblyDefinitions(IEnumerable<TypeDefinition> allTypes, IEnumerable<MethodDefinition> allMethods, IEnumerable<FieldDefinition> allFields, IEnumerable<PropertyDefinition> allProperties) {
	public bool Contains(TypeDefinition type) {
		return allTypes.Contains(type);
	}

	public bool Contains(MethodDefinition method) {
		return allMethods.Contains(method);
	}

	public bool Contains(FieldDefinition field) {
		return allFields.Contains(field);
	}

	public bool Contains(PropertyDefinition property) {
		return allProperties.Contains(property);
	}
}

public record DefinitionMap(Dictionary<AssemblyDefinition, AssemblyDefinitions> defMap) {
	bool Check(Func<AssemblyDefinitions, bool> f) {
		foreach (var d in defMap.Values) {
			if (f(d)) return true;
		}
		return false;
	}

	public TypeDefinition? OriginatesIn(TypeReference reference) {
		if (reference == null) return null;
		var resolved = reference.Resolve();
		return Check((v) => v.Contains(resolved)) ? resolved : null;
	}

	public MethodDefinition? OriginatesIn(MethodReference reference) {
		if (reference == null) return null;
		var resolved = reference.Resolve();
		return Check((v) => v.Contains(resolved)) ? resolved : null;
	}

	public FieldDefinition? OriginatesIn(FieldReference reference) {
		if (reference == null) return null;
		var resolved = reference.Resolve();
		return Check((v) => v.Contains(resolved)) ? resolved : null;
	}
}

public static class DependencyMapper {
	static MethodInfo registerMethod = typeof(DefaultAssemblyResolver).GetMethod("RegisterAssembly", BindingFlags.NonPublic | BindingFlags.Instance);
	static void ResolveRefsRecursive(AssemblyDefinition asm, Dictionary<string, List<AssemblyDefinition>> deps, HashSet<string> alreadyChecked, IAssemblyResolver resolver, bool readPdb) {
		if (!deps.TryGetValue(asm.FullName, out var list)) {
			list = [];
			deps.Add(asm.FullName, list);
		}
		List<AssemblyDefinition> initialDeps = [];
		foreach (var r in asm.MainModule.AssemblyReferences) {
			try
			{
				AssemblyDefinition resolved;
				if (readPdb) {
					resolved = resolver.Resolve(r, new ReaderParameters() { 
						ReadSymbols = true,
						SymbolReaderProvider = new DefaultSymbolReaderProvider(false),
						AssemblyResolver = resolver,
					});
					if (resolved != null && resolver is DefaultAssemblyResolver dar) {
						registerMethod.Invoke(dar, [resolved]);
					}
				}
				else {
					resolved = resolver.Resolve(r, new ReaderParameters() {
						AssemblyResolver = resolver,
					});
					if (resolved != null && resolver is DefaultAssemblyResolver dar) {
						registerMethod.Invoke(dar, [resolved]);
					}
				}
				if (resolved == null) continue;
				list.Add(resolved);
				if (alreadyChecked.Add(resolved.FullName))
				{
					initialDeps.Add(resolved);
				}
			}
			catch (Exception) { }
		}
		foreach (var r in initialDeps) {
			ResolveRefsRecursive(r, deps, alreadyChecked, resolver, readPdb);
		}
	}

	public static Dictionary<string, List<AssemblyDefinition>> ResolveDependencies(AssemblyDefinition asm, IAssemblyResolver resolver, bool readPdb = false) {
		HashSet<string> asmCheck = [];
		Dictionary<string, List<AssemblyDefinition>> deps = [];
		ResolveRefsRecursive(asm, deps, asmCheck, resolver, readPdb);
		return deps;
	}

	public static AssemblyDefinitions BuildDefsFor(AssemblyDefinition assembly) {
		List<TypeDefinition> allTypes = [];
		List<MethodDefinition> allMethods = [];
		List<FieldDefinition> allFields = [];
		List<PropertyDefinition> allProperties = [];
		allTypes.AddRange(assembly.MainModule.GetTypes());
		foreach (var t in allTypes) {
			allMethods.AddRange(t.Methods);
			allFields.AddRange(t.Fields);
			allProperties.AddRange(t.Properties);
		}
		return new(allTypes, allMethods, allFields, allProperties);
	}

	public static DefinitionMap Map(Dictionary<string, List<AssemblyDefinition>> depMap) {
		return Map(depMap, (_) => true);
	}

	public static DefinitionMap Map(Dictionary<string, List<AssemblyDefinition>> depMap, Func<AssemblyDefinition, bool> filter) {
		List<AssemblyDefinition> flattened = [];
		HashSet<string> addedNames = [];
		foreach (var v in depMap.Values) {
			foreach (var asm in v) {
				if (addedNames.Add(asm.FullName)) flattened.Add(asm);
			}
		}
		Dictionary<AssemblyDefinition, AssemblyDefinitions> mapped = [];
		foreach (var asm in flattened) {
			if (filter(asm))
				mapped.Add(asm, BuildDefsFor(asm));
		}
		return new(mapped);
	}

	public static DefinitionMap Map(IEnumerable<AssemblyDefinition> asms, Dictionary<string, List<AssemblyDefinition>> depMap) {
		return Map(asms, depMap, (_) => true);
	}

	public static DefinitionMap Map(IEnumerable<AssemblyDefinition> asms, Dictionary<string, List<AssemblyDefinition>> depMap, Func<AssemblyDefinition, bool> filter) {
		List<AssemblyDefinition> flattened = [];
		HashSet<string> addedNames = [];
		foreach (var asm in asms) {
			if (addedNames.Add(asm.FullName)) flattened.Add(asm);
		}
		foreach (var v in depMap.Values) {
			foreach (var asm in v) {
				if (addedNames.Add(asm.FullName)) flattened.Add(asm);
			}
		}
		Dictionary<AssemblyDefinition, AssemblyDefinitions> mapped = [];
		foreach (var asm in flattened) {
			if (filter(asm))
				mapped.Add(asm, BuildDefsFor(asm));
		}
		return new(mapped);
	}

	public static DefinitionMap Map(IEnumerable<AssemblyDefinition> asms) {
		Dictionary<AssemblyDefinition, AssemblyDefinitions> mapped = [];
		foreach (var asm in asms) {
			var map = BuildDefsFor(asm);
			mapped.Add(asm, map);
		}
		return new(mapped);
	}
}