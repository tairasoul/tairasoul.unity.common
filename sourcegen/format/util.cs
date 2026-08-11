using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace tairasoul.unity.common.sourcegen.format;

static class GeneratorUtil {
	public class VariableTracker {
		Dictionary<string, Type> variables = [];
		Dictionary<object, string> generator = [];
		int generatedCount = 0;

		public bool Exists(object obj) {
			return generator.ContainsKey(obj);
		}

		public string? Get(object obj) {
			if (generator.TryGetValue(obj, out string val))
				return val;
			return null;
		}

		public void Add(object obj, string name) {
			generator.Add(obj, name);
		}

		public bool Exists(string name) {
			return variables.ContainsKey(name);
		}

		public Type? Get(string name) {
			if (variables.TryGetValue(name, out Type val))
				return val;
			return null;
		}

		public void Add(string name, Type type) {
			variables.Add(name, type);
		}

		public string Generate(Type type) {
			string name = "Generated" + generatedCount++.ToString();
			Add(name, type);
			return name;
		}
	}
	public static string Tabs(int amount = 1) {
		if (amount == 0) return "";
		string tb = "	";
		for (int i = 1; i < amount; i++)
			tb += "	";
		return tb;
	}

	public static IEnumerable<string> GetLastTwo(IEnumerable<string> enumerable) {
		if (enumerable.Count() == 0) return [];
		var last = enumerable.Last();
		if (enumerable.Count() == 1) return [last];
		var withoutLast = enumerable.Where((v) => v != last);
		var secondLast = withoutLast.Last();
		return [secondLast, last];
	}
}