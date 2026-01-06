#if BITWRITING_INCLUDE_ASYNC
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
#if BITWRITING_ASYNC_GENERICWRITE
using tairasoul.unity.common.serdes;
#endif
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.bits;

// code from https://github.com/smoogipoo/BinaryBitLib/blob/master/BinaryBitLib/BinaryBitWriter.cs
// modified for generic serialization and for use directly over the network

public class BitWriterAsync(Stream stream) : IDisposable 
{
	public Stream baseStream { get; private set; } = stream;
	private Encoding enc = new UTF8Encoding(false, true);
	byte currentByte;
	int currentBit;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	protected void Dispose(bool disposing)
	{
		if (baseStream != null) {
			baseStream.Flush();
			baseStream.Dispose();
		}
		baseStream = null;
	}

	public BitWriterAsync() : this(new MemoryStream()) {}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	async Task writeCurrentByte() {
		await baseStream.WriteAsync([currentByte], 0, 1);
	}
	async Task Ensure() {
		if (currentBit == 8) {
			await writeCurrentByte();
			currentBit = 0;
			currentByte = 0;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task WriteBit(byte value) {
		await Ensure();
		if (value != 0)
			currentByte |= (byte)(1 << currentBit);
		currentBit++;
	}

	public async Task WriteBits(byte[] value) {
		for (int i = 0; i < value.Length; i++)
			await WriteBit(value[i]);
	}

	public async Task WriteByte(byte value) {
		for (int i = 0; i < 8; i++)
			await WriteBit((byte)(value & (1 << i)));
	}

	public async Task WriteBytes(byte[] value) {
		for (int i = 0; i < value.Length; i++)
			await WriteByte(value[i]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task WriteBool(bool value) {
		await WriteBit(value ? (byte)1 : (byte)0);
	}

	public async Task WriteUInt(uint value, uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit unsigned integer type cannot write to {bits} bits.", nameof(bits));
		for (int i = 0; i < bits; i++)
			await WriteBit((value & ((uint)1 << i)) > 0 ? (byte)1 : (byte)0);
	}

	public async Task WriteULong(ulong value, uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit unsigned integer type cannot write to {bits} bits.", nameof(bits));
		for (int i = 0; i < bits; i++)
			await WriteBit((value & ((ulong)1 << i)) > 0 ? (byte)1 : (byte)0);
	}

	public async Task WriteInt(int value, uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit signed integer type cannot write to {bits} bits.", nameof(bits));
		await WriteBit(((uint)value & ((uint)1 << 31)) > 0 ? (byte)1 : (byte)0);
		bits--;
		await WriteUInt((uint)value, bits);
	}

	public async Task WriteLong(long value, uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit signed integer type cannot write to {bits} bits.", "bits");
		await WriteBit(((ulong)value & ((ulong)1 << (int)bits - 1)) > 0 ? (byte)1 : (byte)0);
		bits--;
		await WriteULong((ulong)value, bits);
	}

	public async Task WriteFloat(float value, uint bits = 32) {
		if (bits == 32) {
			uint tmp = CastUtil.UintFromFloat(value);
			await WriteUInt(tmp, bits);
			return;
		}
		throw new ArgumentException($"32 bit float type cannot write to {bits} bits.", nameof(bits));
	}

	public async Task WriteVarString(string value) {
		byte[] bytes = enc.GetBytes(value);
		await Write7BitEncodedInt(bytes.Length);
		await WriteBytes(bytes);
	}

	public async Task WriteString(string value) {
		byte[] bytes = enc.GetBytes(value);
		await WriteBytes(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task Flush() {
		await writeCurrentByte();
		baseStream.Flush();
		currentBit = 0;
		currentByte = 0;
	}

#if BITWRITING_ASYNC_GENERICWRITE
	public async Task Write<T>(T data) {
		await SerDesMapAsync.Serialize(data, this);
	}
#endif

	/// <summary>
	/// See https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/binarywriter.cs#L414
	/// </summary>
	protected async Task Write7BitEncodedInt(int value)
	{
		uint v = (uint)value;
		while (v >= 0x80)
		{
			await WriteByte((byte)(v | 0x80));
			v >>= 7;
		}
		await WriteByte((byte)v);
	}
}
#endif