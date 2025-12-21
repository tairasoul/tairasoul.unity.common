using System;
using System.Linq;
using System.Reflection;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.embedded;

static class EmbeddedDependencyLoader {
	public static void Init(AppDomain domain, string prefix, string[] assemblyNames) {
		domain.AssemblyResolve += (context, name) =>
		{
			if (assemblyNames.Contains(name.Name)) {
				AssemblyName aname = new(name.Name);
				var primary = Assembly.Load(aname);
				if (primary != null) return primary;
				var bytes = AssemblyUtils.GetResourceBytes($"{prefix}.{aname.Name}.dll");
				if (bytes.Length <= 0) return null;
				return Assembly.Load(bytes);
			}
			return null;
		};
	}
}