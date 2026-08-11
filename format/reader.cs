using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using tairasoul.unity.common.format.attributes;
// using tairasoul.unity.common.format.il;
using tairasoul.unity.common.format.memory;
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.format;

public class FormatReader(Stream sourceStream, int bufferSize) : IDisposable {
	// private delegate object ReadDelegate();

	// private static ReadDelegate readDelegate;

	// static FormatReader() {
	// 	DynamicMethod readMethod = new("readDelegate", typeof(object), [typeof(FormatReader), typeof(Type)], true);
	// 	ILGenerator il = readMethod.GetILGenerator();
	// 	Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
	// 	foreach (var assembly in assemblies) {
	// 		Type[] types = assembly.GetTypes();
	// 		foreach (var type in types) {
	// 			if (type.Namespace == "tairasoul.unity.common.format.items") {
	// 				var @for = type.GetCustomAttribute<FormatMethodsForAttribute>();
	// 				if (@for == null) continue;
	// 				il.Emit(OpCodes.Ldarg_1);

	// 				// var res = BuildInfo(@for.target, type);
	// 				// reg[@for.target] = res;
	// 			}
	// 		}
	// 	}
	// 	// if (!ReadWriteHooks.Patched) {
	// 	// 	ReadWriteHooks.Patch();
	// 	// }
	// }

	private delegate string FastAllocString(int length);

	private static readonly FastAllocString fastAllocString;

	static FormatReader() {
		{
			DynamicMethod fastInitMethod = new("fastStringInit", typeof(string), [typeof(int)], true);
			ILGenerator il = fastInitMethod.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Call, typeof(string).GetMethod("FastAllocateString", BindingFlags.NonPublic | BindingFlags.Static)!);
			il.Emit(OpCodes.Ret);
			fastAllocString = (FastAllocString)fastInitMethod.CreateDelegate(typeof(FastAllocString));
		}
		// ReadWriteHooks.Patch();
	}

	public RingBuffer ring = new(bufferSize);
	readonly int ReadAhead = bufferSize / 4;

	public void Dispose()
	{
		ring.Dispose();
	}

	~FormatReader() {
		Dispose();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EnsureBytes(int count) {
		if (ring.Length < count)
		{
			ring.WriteFromStream(sourceStream, count + Math.Max(0, ReadAhead - ring.Length));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task EnsureBytesAsync(int count) {
		if (ring.Length < count)
		{
			await ring.WriteFromStreamAsync(sourceStream, count + Math.Max(0, ReadAhead - ring.Length));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EnsureBytesExact(int count) {
		if (ring.Length < count)
		{
			ring.WriteFromStream(sourceStream, count);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task EnsureBytesExactAsync(int count) {
		if (ring.Length < count)
		{
			await ring.WriteFromStreamAsync(sourceStream, count);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static unsafe T CastAs<T>(Span<byte> data) where T : unmanaged {
		fixed (byte* ptr = data) {
			return *(T*)ptr;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<byte> ReadBytes(int bytes) {
		EnsureBytes(bytes);
		return ring.Next(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ReadBool() {
		EnsureBytes(1);
		return CastAs<bool>(ring.Next(1));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<bool> ReadBoolAsync() {
		await EnsureBytesAsync(1);
		return CastAs<bool>(ring.Next(1));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte ReadByte() {
		EnsureBytes(1);
		return ring.Next(1)[0];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<byte> ReadByteAsync() {
		await EnsureBytesAsync(1);
		return ring.Next(1)[0];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public sbyte ReadSByte() {
		EnsureBytes(1);
		return CastAs<sbyte>(ring.Next(1));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<sbyte> ReadSByteAsync() {
		await EnsureBytesAsync(1);
		return CastAs<sbyte>(ring.Next(1));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ushort ReadUShort() {
		EnsureBytes(2);
		return CastAs<ushort>(ring.Next(2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<ushort> ReadUShortAsync() {
		await EnsureBytesAsync(2);
		return CastAs<ushort>(ring.Next(2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public short ReadShort() {
		EnsureBytes(2);
		return CastAs<short>(ring.Next(2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<short> ReadShortAsync() {
		await EnsureBytesAsync(2);
		return CastAs<short>(ring.Next(2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public uint ReadUInt() {
		EnsureBytes(4);
		return CastAs<uint>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<uint> ReadUIntAsync() {
		await EnsureBytesAsync(4);
		return CastAs<uint>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int ReadInt() {
		EnsureBytes(4);
		return CastAs<int>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<int> ReadIntAsync() {
		await EnsureBytesAsync(4);
		return CastAs<int>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float ReadFloat() {
		EnsureBytes(4);
		return CastAs<float>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<float> ReadFloatAsync() {
		await EnsureBytesAsync(4);
		return CastAs<float>(ring.Next(4));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ulong ReadULong() {
		EnsureBytes(8);
		return CastAs<ulong>(ring.Next(8));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<ulong> ReadULongAsync() {
		await EnsureBytesAsync(8);
		return CastAs<ulong>(ring.Next(8));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public long ReadLong() {
		EnsureBytes(8);
		return CastAs<long>(ring.Next(8));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<long> ReadLongAsync() {
		await EnsureBytesAsync(8);
		return CastAs<long>(ring.Next(8));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public double ReadDouble() {
		EnsureBytes(8);
		return CastAs<double>(ring.Next(8));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<double> ReadDoubleAsync() {
		await EnsureBytesAsync(8);
		return CastAs<double>(ring.Next(8));
	}

	public string ReadString() {
		EnsureBytes(4);
		int len = CastAs<int>(ring.Next(4));
		if (len > ring.size) {
			int ringCanHold = ring.size - len;
			int remaining = len;
			ExpandableStackMemory stackMem = new(len);
			int currentOffset = 0;
			while (ringCanHold > 0)
			{
				Span<byte> span = stackMem.MemorySpan.Slice(currentOffset);
				EnsureBytesExact(ringCanHold);
				ring.Read(ref span, ringCanHold);
				currentOffset += ringCanHold;
				remaining -= ringCanHold;
			}
			unsafe
			{
				byte* ptr = stackMem.Memory;
				string res = fastAllocString(len);
				fixed (char* dst = res) {
					MemOps.Copy(ptr, dst, len * 2);
				}
				stackMem.Dispose();
				return res;
			}
		}
		else {
			EnsureBytes(len);
			// if (len <= 1024) {
				unsafe {
					fixed (byte* ptr = ring.Next(len)) {
						string res = fastAllocString(len);
						fixed (char* dst = res) {
							MemOps.Copy(ptr, dst, len * 2);
						}
						return res;
					}
				}
			// }
			// else {
			// 	byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
			// 	ring.Next(len).CopyTo(buffer);
			// 	string res = fastAllocString(len);
			// 	unsafe {
			// 		fixed (byte* src = buffer) {
			// 			fixed (char* dst = res) {
			// 				MemOps.Copy(src, dst, len * 2);
			// 			}
			// 		}
			// 	}
			// 	ArrayPool<byte>.Shared.Return(buffer);
			// 	return res;
			// }
		}
	}

	public async ValueTask<string> ReadStringAsync() {
		await EnsureBytesAsync(4);
		int len = CastAs<int>(ring.Next(4));
		if (len > ring.size) {
			int ringCanHold = ring.size - len;
			int remaining = len;
			ExpandableHeapMemory heapMem = new(len);
			int currentOffset = 0;
			while (ringCanHold > 0)
			{
				await EnsureBytesExactAsync(ringCanHold);
				Span<byte> span = heapMem.MemorySpan.Slice(currentOffset);
				ring.Read(ref span, ringCanHold);
				currentOffset += ringCanHold;
				remaining -= ringCanHold;
			}
			unsafe
			{
				byte* ptr = heapMem.Memory;
				string res = fastAllocString(len);
				fixed (char* dst = res) {
					MemOps.Copy(ptr, dst, len * 2);
				}
				heapMem.Dispose();
				return res;
			}
		}
		else {
			await EnsureBytesAsync(len);
			// if (len <= 1024) {
				unsafe {
					fixed (byte* ptr = ring.Next(len)) {
						string res = fastAllocString(len);
						fixed (char* dst = res) {
							MemOps.Copy(ptr, dst, len * 2);
						}
						return res;
					}
				}
			// }
			// else {
			// 	byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
			// 	ring.Next(len).CopyTo(buffer);
			// 	string res = fastAllocString(len);
			// 	unsafe {
			// 		fixed (byte* src = buffer) {
			// 			fixed (char* dst = res) {
			// 				MemOps.Copy(src, dst, len * 2);
			// 			}
			// 		}
			// 	}
			// 	return res;
			// }
		}
	}

// #pragma warning disable CS8603
// #pragma warning disable CS1998

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T Read<T>() {
		return FormatRegistry.Deserialize<T>(this);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<T> ReadAsync<T>() {
		return await FormatRegistry.DeserializeAsync<T>(this);
	}

// #pragma warning restore CS8603
// #pragma warning restore CS1998

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T ReadUnmanaged<T>() where T : unmanaged {
		int size;
		unsafe {
			size = sizeof(T);
		}
		EnsureBytes(size);
		return CastAs<T>(ring.Next(size));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<T> ReadUnmanagedAsync<T>() where T : unmanaged {
		int size;
		unsafe {
			size = sizeof(T);
		}
		await EnsureBytesAsync(size);
		return CastAs<T>(ring.Next(size));
	}
}