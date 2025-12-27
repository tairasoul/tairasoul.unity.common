using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace tairasoul.unity.common.hashing;

// direct port of murmur3 to c#, no attempts at c#-specific optimization yet
static unsafe class Murmur3 {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static ulong fmix64(ulong k) {
		k ^= k >> 33;
		k *= 0xff51afd7ed558ccdUL;
		k ^= k >> 33;
		k *= 0xc4ceb9fe1a85ec53UL;
		k ^= k >> 33;
		return k;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static ulong rotl64(ulong x, sbyte r) {
		return (x << r) | (x >> (64 - r));
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static ulong getblock64(ulong* p, int i) => p[i];
	public static string Hash128(string input) {
		return Hash128(input, 104729);
	}

	public static string Hash128(string input, uint seed) {
		byte[] bytes = Encoding.UTF8.GetBytes(input);
		return Hash128(bytes, seed);
	}

#if MURMUR3_SPANS
	public static string Hash128(ReadOnlySpan<byte> bytes) {
		return Hash128(bytes, 104729);
	}

	public static string Hash128(ReadOnlySpan<byte> bytes, uint seed) {
#else
	public static string Hash128(byte[] bytes) {
		return Hash128(bytes, 104729);
	}

	public static string Hash128(byte[] bytes, uint seed) {
#endif
		int len = bytes.Length;
		fixed (byte* data = bytes)
		{
			int nblocks = len / 16;

			const ulong c1 = 0x87c37b91114253d5UL;
			const ulong c2 = 0x4cf5ad432745937fUL;

			ulong h1 = seed;
			ulong h2 = seed;

			ulong* blocks = (ulong*)data;
			for (int i = 0; i < nblocks; i++)
			{
				ulong k1 = getblock64(blocks, i * 2 + 0);
				ulong k2 = getblock64(blocks, i * 2 + 1);

				k1 *= c1; k1 = rotl64(k1, 31); k1 *= c2; h1 ^= k1;
				h1 = rotl64(h1, 27); h1 += h2; h1 = h1 * 5 + 0x52dce729;
				k2 *= c2; k2 = rotl64(k2, 33); k2 *= c1; h2 ^= k2;
				h2 = rotl64(h2, 31); h2 += h1; h2 = h2 * 5 + 0x38495ab5;
			}

			byte* tail = data + nblocks * 16;
			ulong k1o = 0;
			ulong k2o = 0;
			switch (len & 15)
			{
				case 15: k2o ^= ((ulong)tail[14]) << 48; goto case 14;
				case 14: k2o ^= ((ulong)tail[13]) << 40; goto case 13;
				case 13: k2o ^= ((ulong)tail[12]) << 32; goto case 12;
				case 12: k2o ^= ((ulong)tail[11]) << 24; goto case 11;
				case 11: k2o ^= ((ulong)tail[10]) << 16; goto case 10;
				case 10: k2o ^= ((ulong)tail[9]) << 8; goto case 9;
				case 9:
					k2o ^= ((ulong)tail[8]) << 0;
					k2o *= c2; k2o = rotl64(k2o, 33); k2o *= c1; h2 ^= k2o; goto case 8;
				case 8: k1o ^= ((ulong)tail[7]) << 56; goto case 7;
				case 7: k1o ^= ((ulong)tail[6]) << 48; goto case 6;
				case 6: k1o ^= ((ulong)tail[5]) << 40; goto case 5;
				case 5: k1o ^= ((ulong)tail[4]) << 32; goto case 4;
				case 4: k1o ^= ((ulong)tail[3]) << 24; goto case 3;
				case 3: k1o ^= ((ulong)tail[2]) << 16; goto case 2;
				case 2: k1o ^= ((ulong)tail[1]) << 8; goto case 1;
				case 1:
					k1o ^= ((ulong)tail[0]) << 0;
					k1o *= c1; k1o = rotl64(k1o, 31); k1o *= c2; h1 ^= k1o;
					break;
			}

			h1 ^= (ulong)len; h2 ^= (ulong)len;
			h1 += h2;
			h2 += h1;

			h1 = fmix64(h1);
			h2 = fmix64(h2);

			h1 += h2;
			h2 += h1;

			return $"{h1:x8}{h2:x8}";
		}
	}
}