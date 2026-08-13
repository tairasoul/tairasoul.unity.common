using System;
using System.Buffers;
using System.Diagnostics.Contracts;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.format.memory;

public class RingBuffer : IDisposable
{
	readonly RingBufferMemory memory;
	readonly ExpandableMemory copy;
	readonly int mask;
	public readonly int size;
	public int Length => length;
	int dataStart = 0;
	int length = 0;
	int writePosition = 0;
	bool disposed = false;

	public RingBuffer(int length, int copySize) {
		copy = new(copySize);
		memory = new(length);
		mask = length - 1;
		size = length;
	}

	public RingBuffer(int length) : this(length, length) {}

	~RingBuffer()
	{
		Dispose();
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			memory.Dispose();
			copy.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<byte> Next(int bytes) {
		if (dataStart + bytes <= size) {
			length -= bytes;
			var span = memory.BackingSpan.Slice(dataStart, bytes);
			dataStart = (dataStart + bytes) & mask;
			return span;
		}
		else {
			int bytesToEnd = size - dataStart;
			length -= bytes;
			unsafe {
				byte* ptr = copy.Memory;
				byte* src = memory.BackingPointer;
				copy.EnsureSize(bytes);
				MemOps.Copy(src + dataStart, ptr, bytesToEnd);
				MemOps.Copy(src, ptr + bytesToEnd, bytes - bytesToEnd);
			}
			dataStart = (dataStart + bytes) & mask;
			return copy.MemorySpan.Slice(0, bytes);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Read(ref byte[] output, int length)
	{
		fixed (byte* ptr = output)
		{
			Span<byte> span = new(ptr, length);
			Read(ref span, length);
		}
	}

	public unsafe void Read(ref Span<byte> output, int length)
	{
		byte* src = memory.BackingPointer;
		int bytes = length;
		int bytesToEnd = size - dataStart;
		byte* start = src + dataStart;
		fixed (byte* dst = output)
		{
			if (bytes <= bytesToEnd)
			{
				MemOps.Copy(start, dst, bytes);
			}
			else
			{
				MemOps.Copy(start, dst, bytesToEnd);
				MemOps.Copy(src, dst + bytesToEnd, bytes - bytesToEnd);
			}
			length -= bytes;
			dataStart = (dataStart + bytes) & mask;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Read(ref byte[] output)
	{
		fixed (byte* ptr = output)
		{
			Span<byte> span = new(ptr, output.Length);
			Read(ref span);
		}
	}

	public unsafe void Read(ref Span<byte> output)
	{
		int bytesToEnd = size - writePosition;
		byte* src = memory.BackingPointer;
		byte* start = src + dataStart;
		int bytes = output.Length;
		fixed (byte* dst = output)
		{
			if (bytes <= bytesToEnd)
			{
				MemOps.Copy(start, dst, bytes);
			}
			else
			{
				MemOps.Copy(start, dst, bytesToEnd);
				MemOps.Copy(src, dst + bytesToEnd, bytes - bytesToEnd);
			}
			length -= bytes;
			dataStart = (dataStart + bytes) & mask;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write(Span<byte> bytes)
	{
		Write(bytes, bytes.Length);
	}

	public unsafe void Write(Span<byte> bytes, int count)
	{
		int bytesToEnd = size - writePosition;
		byte* mem = memory.BackingPointer;
		byte* start = mem + writePosition;
		fixed (byte* src = bytes)
		{
			if (count <= bytesToEnd)
			{
				MemOps.Copy(src, start, count);
			}
			else
			{
				MemOps.Copy(src, start, bytesToEnd);
				MemOps.Copy(src + bytesToEnd, mem, count - bytesToEnd);
			}
			length += count;
			writePosition = (writePosition + count) & mask;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteFromStream(Stream stream, int bytes)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);
		int read = stream.Read(buffer, 0, bytes);
		if (read == 0) {
			ArrayPool<byte>.Shared.Return(buffer);
			return;
		}
		Write(buffer, read);
		ArrayPool<byte>.Shared.Return(buffer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task WriteFromStreamAsync(Stream stream, int bytes)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);
		int read = await stream.ReadAsync(buffer, 0, bytes).ConfigureAwait(false);
		if (read == 0) {
			ArrayPool<byte>.Shared.Return(buffer);
			return;
		}
		Write(buffer, read);
		ArrayPool<byte>.Shared.Return(buffer);
	}
}

unsafe struct RingBufferMemory(int length) : IDisposable
{
	readonly byte* backing = MemOps.Alloc(length);
	bool disposed = false;
	public readonly Span<byte> BackingSpan
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => new(backing, length);
	}
	public readonly byte* BackingPointer => backing;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			MemOps.Free(backing);
		}
	}
}