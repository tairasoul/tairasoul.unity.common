using System;
using System.Runtime.CompilerServices;
using System.Text;
using tairasoul.unity.common.hashing;

namespace tairasoul.unity.common.sourcegen.networking.util;

static class StringUtil {
	const string singleTab = "	";
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static string Tabs(int amount = 1) {
		string tb = "";
		for (int i = 0; i < amount; i++)
			tb += singleTab;
		return tb;
	}
	
	public static string Hash(string str) {
    return $"hash_{Murmur3.Hash128(str)}_";
	}
}