using System.Runtime.CompilerServices;

namespace tairasoul.unity.common.util;

static class CastUtil {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe float FloatFromUint(uint ui) {
		return *(float*)&ui;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe uint UintFromFloat(float fl) {
		return *(uint*)&fl;
	}
}