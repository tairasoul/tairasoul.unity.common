using System.IO;
using System.Net;
using System.Net.Sockets;
using tairasoul.unity.common.datastreams;
using tairasoul.unity.common.format;
using tairasoul.unity.common.networking.layer;

namespace tairasoul.unity.common.networking.protocols.udp;

public class UdpConnection : IConnection {
	public IPEndPoint addr;
	public UdpClient client;
	public bool Connected { get; set; } = false;
	public LocalStream readMem = new();
	public FormatReader Reader { get; set; }
	public FormatWriter Writer { get; set; }
	public bool NeedsFlush { get; set; }
	public UdpConnection() {
		Reader = new(readMem, 4096 * 4);
		Writer = new(4096 * 4);
	}

	public void Close() {
		if (Connected) {
			Connected = false;
			Reader.Dispose();
			Writer.Dispose();
			Reader = null;
			Writer = null;
		}
	}

	public void Connect(string host, int port) {
		Connected = true;
	}

	public void Create(Stream read, Stream write)
	{

	}

	public void Flush() {
		if (!NeedsFlush) return;
		var bytes = Writer.Rent();
		client.Send(bytes, bytes.Length, addr);
		Writer.Return(bytes);
		Writer.Reset();
		NeedsFlush = false;
	}
	public void RecheckConnected() {}
}