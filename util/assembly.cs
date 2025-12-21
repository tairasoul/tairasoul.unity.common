using System.IO;
using System.Reflection;

namespace tairasoul.unity.common.util;

static class AssemblyUtils {
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
}