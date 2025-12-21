using System.Runtime.CompilerServices;

namespace tairasoul.unity.common.sourcegen.bits.util;

static class StringUtil {
	const string singleTab = "	";
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string Tabs(int amount = 1) {
		string tb = "";
		for (int i = 0; i < amount; i++)
			tb += singleTab;
		return tb;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe static string Hash(string str) {
		// t1ha hash
    uint hash = 0;
    ReadOnlySpan<byte> bytes = Encoding.UTF8.GetBytes(str);

		fixed (byte* ptr = bytes) {
			byte* p = ptr;
			int length = bytes.Length;
			for (int i = 0; i < length; i++) {
				hash = (hash * 31) + p[i];
			}
		}
    
    string hashString = hash.ToString("x8");
    return $"hash_{hashString}_";
	}
}