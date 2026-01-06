#if BITREADING_INCLUDE_ASYNC
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
#if BITREADING_ASYNC_GENERICREAD
using tairasoul.unity.common.serdes;
#endif
using tairasoul.unity.common.util;

namespace tairasoul.unity.common.bits;

// code from https://github.com/smoogipoo/BinaryBitLib/blob/master/BinaryBitLib/BinaryBitReader.cs
// modified for generic deserialization and for use directly over the network
public class BitReaderAsync : IDisposable
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

	public BitReaderAsync() : this(new MemoryStream()) {}

	public BitReaderAsync(Stream stream) {
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

	async Task Ensure(long bitCount) {
		if (bufferAvailableBits - bitCount < 0)
			while (bufferAvailableBits < bitCount)
			{
				bufferAvailableBits += (long)await baseStream.ReadAsync(buffer, (int)(_bufferCurrentPosition / 8), 8 - (int)(_bufferCurrentPosition / 8)) * 8;
			}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<byte> ReadBit() {
		await Ensure(1);
		bool res = (buffer[bufferCurrentPosition / 8] & (1 << (int)(bufferCurrentPosition % 8))) != 0;
		bufferCurrentPosition++;
		bufferAvailableBits--;
		return res ? (byte)1 : (byte)0;
	}

	public async Task<byte[]> ReadBits(uint count) {
    byte[] bytes = new byte[(int)Math.Ceiling(count / 8f)];
		int bit = 0;

		while (bit < count) {
			bytes[bit / 8] |= (byte)(await ReadBit() << (bit % 8));
			bit++;
		}

		return bytes;
	}

	public async Task<byte> ReadByte() {
		return (await ReadBits(8))[0];
	}

	public async Task<byte[]> ReadBytes(uint count) {
		return await ReadBits(count * 8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public async Task<bool> ReadBool() {
		return await ReadBit() == 1;
	}

	public async Task<uint> ReadUInt(uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit unsigned integer type cannot read from {bits} bits.", nameof(bits));
		byte[] bytes = await ReadBits(bits);
		int ret = 0;
		for (int i = 0; i < bytes.Length; i++)
			ret |= bytes[i] << i * 8;
		return (uint)ret;
	}

	public async Task<ulong> ReadULong(uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit unsigned integer type cannot read from {bits} bits.", nameof(bits));
		byte[] bytes = await ReadBits(bits);
		long ret = 0;
		for (int i = 0; i < bytes.Length; i++)
			ret |= (long)bytes[i] << i * 8;
		return (ulong)ret;
	}

	public async Task<int> ReadInt(uint bits = 32) {
		if (bits > 32 || bits < 1)
			throw new ArgumentException($"32 bit signed integer type cannot read from {bits} bits.", nameof(bits));
		byte msb = await ReadBit();
		bits--;
		uint ret = await ReadUInt(bits);
		if (msb == 1) {
			for (int i = (int)bits; i <= 31; i++)
				ret |= (uint)1 << i;
		}
		return (int)ret;
	}

	public async Task<long> ReadLong(uint bits = 64) {
		if (bits > 64 || bits < 1)
			throw new ArgumentException($"64 bit signed integer type cannot read from {bits} bits.", nameof(bits));
		byte msb = await ReadBit();
		bits--;
		ulong ret = await ReadULong(bits);
		if (msb == 1)
		{
			for (int i = (int)bits; i <= 31; i++)
				ret |= (uint)1 << i;
		}
		return (long)ret;
	}

	public async Task<float> ReadFloat(uint bits = 32) {
		if (bits == 32)
		{
			uint tmp = await ReadUInt(32);
			return CastUtil.FloatFromUint(tmp);
		}
		throw new ArgumentException($"32 bit float type cannot read from {bits} bits.", nameof(bits));
	}

	public async Task<string> ReadString(int length) {
		byte[] charBytes = new byte[length];
		int read = 0;
		while (read < length) {
			charBytes[read++] = await ReadByte();
		}
		return enc.GetString(charBytes);
	}

	public async Task<string> ReadString() {
		int length = await Read7BitEncodedInt();
		return await ReadString(length);
	}

#if BITREADING_ASYNC_GENERICREAD
	public async Task<T> Read<T>() {
		Type type = typeof(T);
		return (T)await SerDesMapAsync.Deserialize(type, this);
	}
#endif

	/// <summary>
	/// See https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/binaryreader.cs#L582
	/// </summary>
	protected async Task<int> Read7BitEncodedInt()
	{
		int count = 0;
		int shift = 0;
		byte b;
		do
		{
			b = await ReadByte();
			count |= (b & 0x7F) << shift;
			shift += 7;
		} while ((b & 0x80) != 0);
		return count;
	}
}
#endif