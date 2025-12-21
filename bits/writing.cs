#if BITWRITING_INCLUDE_SYNC
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
#if BITWRITING_SYNC_GENERICWRITE
using tairasoul.unity.common.serdes;
#endif
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.bits;

// code from https://github.com/smoogipoo/BinaryBitLib/blob/master/BinaryBitLib/BinaryBitWriter.cs
// modified for MessagePack-like serialization and for use directly over the network
public class BitWriter(Stream stream) : IDisposable 
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

	public BitWriter() : this(new MemoryStream()) {}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void writeCurrentByte() {
		baseStream.WriteByte(currentByte);
	}
	void Ensure() {
		if (currentBit == 8) {
			writeCurrentByte();
			currentBit = 0;
			currentByte = 0;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteBit(byte value) {
		Ensure();
		if (value != 0)
			currentByte |= (byte)(1 << currentBit);
		currentBit++;
	}

	public void WriteBits(byte[] value) {
		for (int i = 0; i < value.Length; i++)
			WriteBit(value[i]);
	}

	public void WriteByte(byte value) {
		for (int i = 0; i < 8; i++)
			WriteBit((byte)(value & (1 << i)));
	}

	public void WriteBytes(byte[] value) {
		for (int i = 0; i < value.Length; i++)
			WriteByte(value[i]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteBool(bool value) {
		WriteBit(value ? (byte)1 : (byte)0);
	}

	public void WriteUInt(uint value, uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit unsigned integer type cannot write to {bits} bits.", nameof(bits));
		for (int i = 0; i < bits; i++)
			WriteBit((value & ((uint)1 << i)) > 0 ? (byte)1 : (byte)0);
	}

	public void WriteULong(ulong value, uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit unsigned integer type cannot write to {bits} bits.", nameof(bits));
		for (int i = 0; i < bits; i++)
			WriteBit((value & ((ulong)1 << i)) > 0 ? (byte)1 : (byte)0);
	}

	public void WriteInt(int value, uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit signed integer type cannot write to {bits} bits.", nameof(bits));
		WriteBit(((uint)value & ((uint)1 << 31)) > 0 ? (byte)1 : (byte)0);
		bits--;
		WriteUInt((uint)value, bits);
	}

	public void WriteLong(long value, uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit signed integer type cannot write to {bits} bits.", "bits");
		WriteBit(((ulong)value & ((ulong)1 << (int)bits - 1)) > 0 ? (byte)1 : (byte)0);
		bits--;
		WriteULong((ulong)value, bits);
	}

	public void WriteFloat(float value, uint bits = 32) {
		if (bits == 32) {
			uint tmp = CastUtil.UintFromFloat(value);
			WriteUInt(tmp, bits);
			return;
		}
		throw new ArgumentException($"32 bit float type cannot write to {bits} bits.", nameof(bits));
	}

	public void WriteVarString(string value) {
		byte[] bytes = enc.GetBytes(value);
		Write7BitEncodedInt(bytes.Length);
		WriteBytes(bytes);
	}

	public void WriteString(string value) {
		byte[] bytes = enc.GetBytes(value);
		WriteBytes(bytes);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Flush() {
		writeCurrentByte();
		baseStream.Flush();
		currentBit = 0;
		currentByte = 0;
	}

#if BITWRITING_SYNC_GENERICWRITE
	public void Write<T>(T data) {
		SerDesMap.Serialize(data, this);
	}
#endif

	/// <summary>
	/// See https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/binarywriter.cs#L414
	/// </summary>
	protected void Write7BitEncodedInt(int value)
	{
		uint v = (uint)value;
		while (v >= 0x80)
		{
			WriteByte((byte)(v | 0x80));
			v >>= 7;
		}
		WriteByte((byte)v);
	}
}
#endif