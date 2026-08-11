using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace tairasoul.unity.common.util;

// exists to both create custom extremely unsafe methods (if any would be useful) via Fody and to backport newer Unsafe features like SkipInit (ensuring they exist in most possible environments)
public class CustomUnsafe {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void SkipInit<T>(out T value) where T : struct {
		throw null;
	}
}

// this is _slightly_ faster than just doing FormatterServices.GetUninitializedObject based off benchmarks
// not worth trying to optimize further though, not sure if it's even possible to optimize further (for classes)
public static class UninitializedFactory<T> {
	public static readonly Func<T> Create = () =>
	{
		return (T)FormatterServices.GetUninitializedObject(typeof(T));
	};
}