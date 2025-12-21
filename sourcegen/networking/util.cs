using System;
using System.Runtime.CompilerServices;
using System.Text;

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
	
	public unsafe static string Hash(string str) {
		// fnv hash
		// todo: make t1ha2 hash
		// (because trying to use a hash library doesnt work??)
		ulong hash = 14695981039346656037;
    ReadOnlySpan<byte> bytes = Encoding.Unicode.GetBytes(str);

		fixed (byte* ptr = bytes) {
			int length = bytes.Length;
			for (int i = 0; i < length; i++) {
				hash ^= ptr[i];
				hash = (hash * 1099511628211);
			}
		}
    
    string hashString = hash.ToString("x8");
    return $"hash_{hashString}_";
	}
}