using System.IO;
using System.Net.Sockets;
using tairasoul.unity.common.format;
using tairasoul.unity.common.networking.layer;

namespace tairasoul.unity.common.networking.protocols.tcp;

public class TcpConnection : IConnection
{
	public TcpClient client;
	public bool Connected { get; set; } = false;
	public Stream ReadStream { get; set; }
	public Stream WriteStream { get; set; }
	public FormatReader Reader { get; set; }
	public FormatWriter Writer { get; set; }
	public bool NeedsFlush { get; set; }
	public TcpConnection() {
		client = new();
		client.NoDelay = true;
		client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, true);
	}

	public void Close()
	{
		if (Connected) {
			client.Client.Close();
			Connected = false;
			ReadStream = null;
			WriteStream = null;
			Reader.Dispose();
			Writer.Dispose();
			Reader = null;
			Writer = null;
		}
	}

	public void Connect(string host, int port)
	{
		client.Connect(host, port);
		Connected = true;
		ReadStream = client.GetStream();
		WriteStream = client.GetStream();
		Reader = new(ReadStream, 4096 * 4);
		Writer = new(4096 * 4);
	}

	public void Create(Stream read, Stream write)
	{
		Connected = true;
		ReadStream = read;
		WriteStream = write;
		Reader = new(ReadStream, 4096 * 4);
		Writer = new(4096 * 4);
	}

	public void Flush() {
		if (!NeedsFlush) return;
		Writer.WriteToStream(WriteStream);
		Writer.Reset();
		NeedsFlush = false;
	}

	public void RecheckConnected() {
		if (client.Connected != Connected) {
			if (Connected && !client.Connected) {
				Close();
			}
			Connected = client.Connected;
		}
	}
}