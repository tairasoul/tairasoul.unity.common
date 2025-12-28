using System.Reflection;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

static class Typefinder {
	public static Type FindType(string fullName) {
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
			Type type = assembly.GetType(fullName);
			if (type != null)
				return type;
		}
		return null;
	}
}