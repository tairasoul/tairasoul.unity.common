#if BITREADING_INCLUDE_SYNC
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
#if BITREADING_SYNC_GENERICREAD
using tairasoul.unity.common.serdes;
#endif
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.bits;

// code from https://github.com/smoogipoo/BinaryBitLib/blob/master/BinaryBitLib/BinaryBitReader.cs
// modified for MessagePack-like deserialization and for use directly over the network

public class BitReader : IDisposable
{
	public Stream baseStream { get; private set; }
	private Encoding enc = new UTF8Encoding(false, true);
	byte[] buffer;
	private long bufferAvailableBits;
	private long _bufferCurrentPosition;
	private long bufferCurrentPosition
	{
		get { return _bufferCurrentPosition; }
		set
		{
			_bufferCurrentPosition = value;
			if (_bufferCurrentPosition >= 64)
				_bufferCurrentPosition = 0;
		}
	}

	public BitReader() : this(new MemoryStream()) {}

	public BitReader(Stream stream) {
		baseStream = stream;
		buffer = [];
		Array.Resize(ref buffer, 8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Reset() {
		buffer = [];
		Array.Resize(ref buffer, 8);
		bufferAvailableBits = 0;
		bufferCurrentPosition = 0;
	}

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

	void Ensure(long bitCount) {
		if (bufferAvailableBits - bitCount < 0)
			while (bufferAvailableBits < bitCount)
			{
				bufferAvailableBits += (long)baseStream.Read(buffer, (int)(_bufferCurrentPosition / 8), 8 - (int)(_bufferCurrentPosition / 8)) * 8;
			}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte ReadBit() {
		Ensure(1);
		bool res = (buffer[bufferCurrentPosition / 8] & (1 << (int)(bufferCurrentPosition % 8))) != 0;
		bufferCurrentPosition++;
		bufferAvailableBits--;
		return res ? (byte)1 : (byte)0;
	}

	public byte[] ReadBits(uint count) {
    byte[] bytes = new byte[(int)Math.Ceiling(count / 8f)];
		int bit = 0;

		while (bit < count) {
			bytes[bit / 8] |= (byte)(ReadBit() << (bit % 8));
			bit++;
		}

		return bytes;
	}

	public byte ReadByte() {
		return ReadBits(8)[0];
	}

	public byte[] ReadBytes(uint count) {
		return ReadBits(count * 8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool ReadBool() {
		return ReadBit() == 1;
	}

	public uint ReadUInt(uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit unsigned integer type cannot read from {bits} bits.", nameof(bits));
		byte[] bytes = ReadBits(bits);
		int ret = 0;
		for (int i = 0; i < bytes.Length; i++)
			ret |= bytes[i] << i * 8;
		return (uint)ret;
	}

	public ulong ReadULong(uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit unsigned integer type cannot read from {bits} bits.", nameof(bits));
		byte[] bytes = ReadBits(bits);
		long ret = 0;
		for (int i = 0; i < bytes.Length; i++)
			ret |= (long)bytes[i] << i * 8;
		return (ulong)ret;
	}

	public int ReadInt(uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit signed integer type cannot read from {bits} bits.", nameof(bits));
		byte msb = ReadBit();
		bits--;
		uint ret = ReadUInt(bits);
		if (msb == 1) {
			for (int i = (int)bits; i <= 31; i++)
				ret |= (uint)1 << i;
		}
		return (int)ret;
	}

	public long ReadLong(uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit signed integer type cannot read from {bits} bits.", nameof(bits));
		byte msb = ReadBit();
		bits--;
		ulong ret = ReadULong(bits);
		if (msb == 1)
		{
			for (int i = (int)bits; i <= 31; i++)
				ret |= (uint)1 << i;
		}
		return (long)ret;
	}

	public float ReadFloat(uint bits = 32) {
		if (bits == 32)
		{
			uint tmp = ReadUInt(32);
			return CastUtil.FloatFromUint(tmp);
		}
		throw new ArgumentException($"32 bit float type cannot read from {bits} bits.", nameof(bits));
	}

	public string ReadString(int length) {
		byte[] charBytes = new byte[length];
		int read = 0;
		while (read < length) {
			charBytes[read++] = ReadByte();
		}
		return enc.GetString(charBytes);
	}

	public string ReadString() {
		int length = Read7BitEncodedInt();
		return ReadString(length);
	}

#if BITREADING_SYNC_GENERICREAD
	public T Read<T>() {
		Type type = typeof(T);
		return (T)SerDesMap.Deserialize(type, this);
	}
#endif

	/// <summary>
	/// See https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/binaryreader.cs#L582
	/// </summary>
	protected int Read7BitEncodedInt()
	{
		int count = 0;
		int shift = 0;
		byte b;
		do
		{
			b = ReadByte();
			count |= (b & 0x7F) << shift;
			shift += 7;
		} while ((b & 0x80) != 0);
		return count;
	}
}
#endif