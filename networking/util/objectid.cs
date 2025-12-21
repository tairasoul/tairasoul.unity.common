using System.Runtime.CompilerServices;

namespace tairasoul.unity.common.networking.util;

class ObjectIdUtils {
	const int PlayerIdBitshift = 52;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ushort ExtractPlayerID(ulong id)
	{
		return (ushort)(id >> PlayerIdBitshift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ulong ExtractObjectID(ulong id) {
		return id & 0xFFFFFFFFFFFFL;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ulong CreateID(ushort player, ulong counter) {
		return ((ulong)player << PlayerIdBitshift) | (counter & 0xFFFFFFFFFFFFL);
	}
}