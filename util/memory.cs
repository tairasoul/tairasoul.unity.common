using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#if UNITY_ENVIRONMENT
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace tairasoul.unity.common.util;

unsafe static class MemOps {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* Alloc(int length) {
#if UNITY_ENVIRONMENT
		return (byte*)UnsafeUtility.Malloc(length, 1, Allocator.Persistent);
#elif NATIVE_MEMORY
		return (byte*)NativeMemory.Alloc((nuint)length);
#else
		return (byte*)Marshal.AllocHGlobal(length);
#endif
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Free(void* ptr) {
#if UNITY_ENVIRONMENT
		UnsafeUtility.Free(ptr, Allocator.Persistent);
#elif NATIVE_MEMORY
		NativeMemory.Free(ptr);
#else
		Marshal.FreeHGlobal((IntPtr)ptr);
#endif
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Copy(void* src, void* dst, int bytes) {
#if UNITY_ENVIRONMENT
		UnsafeUtility.MemCpy(dst, src, bytes);
#else
		Unsafe.CopyBlock(dst, src, (uint)bytes);
#endif
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* Realloc(void* oldMem, int oldMemLength, int newLength) {
#if UNITY_ENVIRONMENT
		void* newMem = UnsafeUtility.Malloc(newLength, 1, Allocator.Persistent);
		UnsafeUtility.MemCpy(newMem, oldMem, oldMemLength);
		UnsafeUtility.Free(oldMem, Allocator.Persistent);
		return (byte*)newMem;
#elif NATIVE_MEMORY
		return (byte*)NativeMemory.Realloc(oldMem, (nuint)newLength);
#else
		return (byte*)Marshal.ReAllocHGlobal((IntPtr)oldMem, (IntPtr)newLength);
#endif
	}

// 	[MethodImpl(MethodImplOptions.AggressiveInlining)]
// 	public static int SizeOf<T>() where T : unmanaged
// 	{
// #if UNITY_ENVIRONMENT
// 		return UnsafeUtility.SizeOf<T>();
// #else
// 		return Unsafe.SizeOf<T>();
// #endif
// 	}
}