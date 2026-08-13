using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
// using tairasoul.unity.common.format.il;
using tairasoul.unity.common.format.memory;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.format;

#pragma warning disable CS9124

public class FormatWriter(int baseLength) : IDisposable {
	// private delegate void WriteDelegate(object data);

	// private static WriteDelegate writeDelegate;

	// static FormatWriter() {
	// 	ReadWriteHooks.Patch();
	// }

	ExpandableMemory memory = new(baseLength);
	public int Length {
		get;
		private set;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	~FormatWriter() {
		Dispose();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Reset(bool shrink = false) {
		Length = 0;
		if (shrink) {
			memory.Shrink();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		memory.Dispose();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void WriteToStream(Stream stream) {
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Length);
		fixed (byte* dst = buffer) {
			MemOps.Copy(memory, dst, Length);
		}
		stream.Write(buffer, 0, Length);
		ArrayPool<byte>.Shared.Return(buffer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task WriteToStreamAsync(Stream stream) {
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Length);
		unsafe {
			fixed (byte* dst = buffer) {
				MemOps.Copy(memory, dst, Length);
			}
		}
		await stream.WriteAsync(buffer, 0, Length);
		ArrayPool<byte>.Shared.Return(buffer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe byte[] Rent() {
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Length);
		fixed (byte* dst = buffer) {
			MemOps.Copy(memory, dst, Length);
		}
		return buffer;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Return(byte[] rented) {
		ArrayPool<byte>.Shared.Return(rented);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	unsafe void EnsureSize(int size) {
		if (memory.Length - Length <= size) {
			memory.Realloc(memory.Length * 2);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(string data) {
		int length = data.Length;
		EnsureSize(length + 4);
		Unsafe.Write(memory + Length, length);
		Length += 4;
		fixed (char* src = data) {
			MemOps.Copy(src, memory + Length, length);
		}
		Length += length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(Span<byte> data) {
		int length = data.Length;
		EnsureSize(length);
		fixed (byte* src = data)
		{
			MemOps.Copy(src, memory + Length, length);
		}
		Length += length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(bool data) {
		EnsureSize(1);
		Unsafe.Write(memory + Length, data);
		Length += 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(byte data) {
		EnsureSize(1);
		Unsafe.Write(memory + Length, data);
		Length += 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(sbyte data) {
		EnsureSize(1);
		Unsafe.Write(memory + Length, data);
		Length += 1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(ushort data) {
		EnsureSize(2);
		Unsafe.Write(memory + Length, data);
		Length += 2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(short data) {
		EnsureSize(2);
		Unsafe.Write(memory + Length, data);
		Length += 2;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(uint data) {
		EnsureSize(4);
		Unsafe.Write(memory + Length, data);
		Length += 4;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(int data) {
		EnsureSize(4);
		Unsafe.Write(memory + Length, data);
		Length += 4;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(float data) {
		EnsureSize(4);
		Unsafe.Write(memory + Length, data);
		Length += 4;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(ulong data) {
		EnsureSize(8);
		Unsafe.Write(memory + Length, data);
		Length += 4;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(long data) {
		EnsureSize(8);
		Unsafe.Write(memory + Length, data);
		Length += 4;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Write(double data) {
		EnsureSize(8);
		Unsafe.Write(memory + Length, data);
		Length += 8;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write<T>(T data) {
		FormatRegistry.Serialize<T>(data, this);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void WriteUnmanaged<T>(T data) where T : unmanaged {
		int size = sizeof(T);
		EnsureSize(size);
		Unsafe.Write(memory + Length, data);
		Length += size;
	}
}