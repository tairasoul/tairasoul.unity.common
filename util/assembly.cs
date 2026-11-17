using System.IO;
using System.Reflection;

namespace tairasoul.unity.common.util;

public static class AssemblyUtils {
  public static byte[] GetResourceBytes(Assembly assembly, string resource) {
    using Stream? stream = assembly.GetManifestResourceStream(resource);
    if (stream == null) return [];
    using MemoryStream ms = new();
    stream.CopyTo(ms);
    return ms.ToArray();
  }

  public static byte[] GetResourceBytes(string resource) {
    return GetResourceBytes(Assembly.GetCallingAssembly(), resource);
  }

  public static bool ResourceExists(Assembly assembly, string resource) {
    using Stream? stream = assembly.GetManifestResourceStream(resource);
    if (stream == null) return false;
		return true;
	}

  public static bool ResourceExists(string resource) {
		return ResourceExists(Assembly.GetCallingAssembly(), resource);
	}
}