using System;
using System.Linq;
using System.Reflection;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.embedded;

static class EmbeddedDependencyLoader {
	public static void Init(AppDomain domain, string prefix, string[] assemblyNames) {
		domain.AssemblyResolve += (context, name) =>
		{
			AssemblyName aname = new(name.Name);
			if (assemblyNames.Contains(aname.Name)) {
				try
				{
					var primary = Assembly.Load(aname);
					if (primary != null) return primary;
				}
				catch {
					var bytes = AssemblyUtils.GetResourceBytes($"{prefix}.{aname.Name}.dll");
					if (bytes.Length <= 0) return null;
					return Assembly.Load(bytes);
				}
			}
			return null;
		};
	}
}