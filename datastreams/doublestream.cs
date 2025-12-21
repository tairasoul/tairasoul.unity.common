using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace tairasoul.unity.common.datastreams;

class DoubleStream(Stream read, Stream write) : Stream {
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
		set {
			write.Position = value;
			read.Position = value;
		}
	}

	public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		await write.WriteAsync(buffer, offset, count, cancellationToken);
	}
	public new async Task WriteAsync(byte[] buffer, int offset, int count)
	{
		await WriteAsync(buffer, offset, count, _cts.Token);
	}

	public override void Write(byte[] buffer, int offset, int count)
		=> WriteAsync(buffer, offset, count, _cts.Token).GetAwaiter().GetResult();
	public override void Flush() {
		write.Flush();
	}
	public override int Read(byte[] buffer, int offset, int count)
		=> ReadAsync(buffer, offset, count, _cts.Token).GetAwaiter().GetResult();

	public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return await read.ReadAsync(buffer, offset, count, cancellationToken);
	}
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) {
		read.SetLength(value);
		write.SetLength(value);
	}
}