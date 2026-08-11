using System;
using System.Runtime.CompilerServices;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.format.memory;

unsafe struct ExpandableHeapMemory(int baseLength) : IDisposable
{
	byte* memory = MemOps.Alloc(baseLength);
	bool disposed = false;
	public int Length = baseLength;
	public readonly byte* Memory => memory;
	public readonly Span<byte> MemorySpan => new(memory, Length);

	public static implicit operator byte*(ExpandableHeapMemory memory)
	{
		return memory.memory;
	}

	public static byte* operator +(ExpandableHeapMemory lh, int rh)
	{
		return lh.memory + rh;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Realloc(int newLength)
	{
		memory = MemOps.Realloc(memory, Length, newLength);
		Length = newLength;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Grow(int byBytes) {
		Realloc(Length + byBytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EnsureSize(int bytes) {
		if (Length < bytes) {
			Realloc(bytes);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Shrink()
	{
		MemOps.Free(memory);
		memory = MemOps.Alloc(baseLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			MemOps.Free(memory);
		}
	}
}

unsafe ref struct ExpandableStackMemory(int baseLength) : IDisposable
{
	byte* memory = MemOps.Alloc(baseLength);
	bool disposed = false;
	public int Length = baseLength;
	public readonly byte* Memory => memory;
	public readonly Span<byte> MemorySpan => new(memory, Length);

	public static implicit operator byte*(ExpandableStackMemory memory)
	{
		return memory.memory;
	}

	public static byte* operator +(ExpandableStackMemory lh, int rh)
	{
		return lh.memory + rh;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Realloc(int newLength)
	{
		memory = MemOps.Realloc(memory, Length, newLength);
		Length = newLength;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Grow(int byBytes) {
		Realloc(Length + byBytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EnsureSize(int bytes) {
		if (Length < bytes) {
			Realloc(bytes);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Shrink()
	{
		MemOps.Free(memory);
		memory = MemOps.Alloc(baseLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			MemOps.Free(memory);
		}
	}
}