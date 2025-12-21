using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace tairasoul.unity.common.datastreams;

class LocalStream : Stream
{
	private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
	private readonly CancellationTokenSource _cts = new();

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override bool CanTimeout => true;
	public override int ReadTimeout { get; set; } = 30000;
	public override long Length => throw new NotSupportedException();
	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}
	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		while (await _channel.Reader.WaitToReadAsync(cancellationToken))
		{
			if (_channel.Reader.TryRead(out var result))
			{
				if (result.Length > count)
				{
					Buffer.BlockCopy(result, 0, buffer, offset, count);
					_channel.Writer.TryWrite([.. result.Skip(count)]);
					return count;
				}
				Buffer.BlockCopy(result, 0, buffer, offset, result.Length);
				return result.Length;
			}
		}
		return 0;
	}
	public override int Read(byte[] buffer, int offset, int count)
		=> ReadAsync(buffer, offset, count, _cts.Token).GetAwaiter().GetResult();
	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		var data = new byte[count];
		Buffer.BlockCopy(buffer, offset, data, 0, count);
		await _channel.Writer.WriteAsync(data, cancellationToken);
	}
	public new async Task WriteAsync(byte[] buffer, int offset, int count)
	{
		await WriteAsync(buffer, offset, count, _cts.Token);
	}

	public override void Write(byte[] buffer, int offset, int count)
		=> WriteAsync(buffer, offset, count, _cts.Token).GetAwaiter().GetResult();
	public override void Flush() { }
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}