using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static tairasoul.unity.common.sourcegen.networking.GeneratorUtil;
using sdi = System.Collections.Immutable.ImmutableArray<tairasoul.unity.common.sourcegen.networking.SymbolData>;

namespace tairasoul.unity.common.sourcegen.networking;

// this really needs to be redone to not be horrid

enum InternalCorrelationType {
	PacketBatchEnd,
	IdRelay,
	Connect,
	Disconnect,
	PlayerConnected,
	None
}

enum AttributeUsed
{
	CorrelatesTo,
	CorrelatesToInternal,
	ServerRelay,
	Reliability,
	PacketTypeIdentifier,
	ImplementUnreliableRead,
	ImplementReliableRead,
	ImplementUnreliableHeaderWrite,
	ImplementReliableHeaderWrite,
	ImplementReliabilityGet,
	ImplementServerRelay
}

record SymbolData(AttributeUsed used, string name, string qualName, string ns, ImmutableArray<string> arguments, bool isServer = false);
record Types(sdi correlates, sdi icorrelates, sdi relay, sdi reliability, sdi packetident);

class NetGen {
	const string CodeBaseIncludes = 
	"""
	using tairasoul.unity.common.format;
	using tairasoul.unity.common.networking.gentypes;
	using tairasoul.unity.common.networking.interfaces;
	using tairasoul.unity.common.networking.util;
	using tairasoul.unity.common.util;
	using System.CodeDom.Compiler;
	using System;
	using System.Collections.Generic;
	using System.Threading.Tasks;
	""";
	const string GeneratedCodeData = "\"tairasoul.unity.common.sourcegen.networking\", \"0.1.0\"";
	public static void Codegen(SourceProductionContext context, Types types)
	{
		if (types.packetident.Length == 0) {				
			context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
				"NET001",
				"No packet identifier",
				"No packet identifier enum was declared.",
				"Generation",
				DiagnosticSeverity.Error,
				true
			),
			Location.None));
			return;
		}
		GenerateInternalStructs(context, types);
		GenerateTcpUdpHybridFactory(context, types);
		GenerateTcpClient(context, types);
		GenerateUdpClient(context, types);
		GenerateTcpServer(context, types);
		GenerateUdpServer(context, types);
		GenerateHostLayer(context, types);
	}

	static bool IsReliable(Types types, SymbolData symData) {
		return types.reliability.Any((v) => symData.arguments.First() == v.name && v.arguments.Any(v => v == "Reliable"));
	}

	static bool IsUnreliable(Types types, SymbolData symData) {
		return types.reliability.Any((v) => symData.arguments.First() == v.name && v.arguments.Any(v => v == "Unreliable"));
	}

	static string GetInternalCorrelation(Types types, string correl) {
		foreach (var ic in types.icorrelates) {
			if (ic.arguments.First().EndsWith(correl))
				return ic.qualName;
		}
		return "";
	}

	static void GenerateTcpClient(SourceProductionContext prodContext, Types types) {
		// string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder client = new();
		client.AppendLine(CodeBaseIncludes);
		client.AppendLine("using System.Threading;");
		client.AppendLine($"using {types.packetident.First().ns};");
		client.AppendLine("namespace tairasoul.unity.common.networking.clients;");
		client.AppendLine("partial class ClientTcp : IClient");
		client.AppendLine("{");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}void CheckSpecialAction(object packet, FormatReader reader) {{");
		// var batchEnd = GetInternalCorrelation(types, "BatchEnd");
		// if (batchEnd != null) {
		// 	client.AppendLine($"{Tabs(2)}if (packet is {packetTypeFullName}.{batchEnd}) reader.Reset();");
		// }
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void Flush() {{");
		client.AppendLine($"{Tabs()}if (!needsFlush) return;");
		client.AppendLine($"{Tabs()}needsFlush = false;");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		// if (batchEnd != "") {
		// 	client.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{batchEnd});");
		// }
		client.AppendLine($"{Tabs(3)}writer.WriteToStream(stream);");
		client.AppendLine($"{Tabs(3)}writer.Reset();");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}public void Disconnect() {{");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "Disconnect") != "") {
			client.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "Disconnect")});");
		}
		client.AppendLine($"{Tabs(3)}writer.WriteToStream(stream);");
		client.AppendLine($"{Tabs(3)}writer.Reset();");
		client.AppendLine($"{Tabs(3)}client.Close();");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine("}");
		prodContext.AddSource("network/clients.tcp.g.cs", client.ToString());
	}

	static void GenerateUdpClient(SourceProductionContext prodContext, Types types) {
		// string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder client = new();
		client.AppendLine(CodeBaseIncludes);
		client.AppendLine($"using {types.packetident.First().ns};");
		client.AppendLine("namespace tairasoul.unity.common.networking.clients;");
		client.AppendLine("partial class ClientUdp : IClient");
		client.AppendLine("{");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}void CheckSpecialAction(object packet, FormatReader reader) {{");
		// var batchEnd = GetInternalCorrelation(types, "BatchEnd");
		// if (batchEnd != "") {
		// 	client.AppendLine($"{Tabs(2)}if (packet is {packetTypeFullName}.{batchEnd}) reader.Reset();");
		// }
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void Flush() {{");
		client.AppendLine($"{Tabs()}if (!needsFlush) return;");
		client.AppendLine($"{Tabs()}needsFlush = false;");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		// if (batchEnd != "") {
		// 	client.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{batchEnd});");
		// }
		client.AppendLine($"{Tabs(3)}byte[] bytes = writer.Rent();");
		client.AppendLine($"{Tabs(3)}client.Send(bytes, bytes.Length, host, port);");
		client.AppendLine($"{Tabs(3)}writer.Return(bytes);");
		client.AppendLine($"{Tabs(3)}writer.Reset();");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine("}");
		prodContext.AddSource("network/clients.udp.g.cs", client.ToString());
	}

	static void GenerateTcpServer(SourceProductionContext prodContext, Types types) {
		// string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder server = new();
		server.AppendLine(CodeBaseIncludes);
		server.AppendLine($"using {types.packetident.First().ns};");
		server.AppendLine("namespace tairasoul.unity.common.networking.servers;");
		server.AppendLine("partial class ServerTcp : IServer {");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}void CheckSpecialAction(object packet, FormatReader reader) {{");
		// var batchEnd = GetInternalCorrelation(types, "BatchEnd");
		// if (batchEnd != "") {
		// 	server.AppendLine($"{Tabs(2)}if (packet is {packetTypeFullName}.{batchEnd}) reader.Reset();");
		// }
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void Flush() {{");
		server.AppendLine($"{Tabs(2)}foreach (var conn in players.Values) {{");
		server.AppendLine($"{Tabs(3)}if (!conn.needsFlush) continue;");
		server.AppendLine($"{Tabs(3)}conn.needsFlush = false;");
		server.AppendLine($"{Tabs(3)}ActionQueue.Enqueue(() => {{");
		// if (batchEnd != "") {
		// 	server.AppendLine($"{Tabs(4)}conn.writer.Write({packetTypeFullName}.{batchEnd});");
		// }
		server.AppendLine($"{Tabs(4)}conn.writer.WriteToStream(conn.stream);");
		server.AppendLine($"{Tabs(4)}conn.writer.Reset();");
		server.AppendLine($"{Tabs(3)}}});");
		server.AppendLine($"{Tabs(2)}}}");
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine("}");
		prodContext.AddSource("network/servers.tcp.g.cs", server.ToString());
	}

	static void GenerateUdpServer(SourceProductionContext prodContext, Types types) {
		// string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder server = new();
		server.AppendLine(CodeBaseIncludes);
		server.AppendLine($"using {types.packetident.First().ns};");
		server.AppendLine("namespace tairasoul.unity.common.networking.servers;");
		server.AppendLine("partial class ServerUdp : IServer {");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}void CheckSpecialAction(object packet, FormatReader reader) {{");
		// var batchEnd = GetInternalCorrelation(types, "BatchEnd");
		// if (batchEnd != "") {
		// 	server.AppendLine($"{Tabs(2)}if (packet is {packetTypeFullName}.{batchEnd}) reader.Reset();");
		// }
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void Flush() {{");
		server.AppendLine($"{Tabs(2)}foreach (var conn in players.Values) {{");
		server.AppendLine($"{Tabs(3)}if (!conn.requiresFlush) continue;");
		server.AppendLine($"{Tabs(3)}conn.requiresFlush = false;");
		server.AppendLine($"{Tabs(3)}ActionQueue.Enqueue(() => {{");
		// if (batchEnd != "") {
		// 	server.AppendLine($"{Tabs(4)}conn.writer.Write({packetTypeFullName}.{batchEnd});");
		// }
		server.AppendLine($"{Tabs(4)}byte[] bytes = conn.writer.Rent();");
		server.AppendLine($"{Tabs(4)}client.Send(bytes, bytes.Length, conn.addr);");
		server.AppendLine($"{Tabs(4)}conn.writer.Return(bytes);");
		server.AppendLine($"{Tabs(4)}conn.writer.Reset();");
		server.AppendLine($"{Tabs(3)}}});");
		server.AppendLine($"{Tabs(2)}}}");
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine("}");
		prodContext.AddSource("network/servers.udp.g.cs", server.ToString());
	}

	static void GenerateTcpUdpHybridFactory(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder hybrid = new();
		hybrid.AppendLine(CodeBaseIncludes);
		hybrid.AppendLine($"using tairasoul.unity.common.networking.clients;");
		hybrid.AppendLine($"using tairasoul.unity.common.networking.servers;");
		hybrid.AppendLine("namespace tairasoul.unity.common.networking.factories;");
		hybrid.AppendLine($"partial class TcpUdpHybridFactory : ITransportFactory {{");
		hybrid.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		hybrid.AppendLine($"{Tabs()}public IServer CreateUnreliableServer(IServer reliable, int port) {{");
		hybrid.AppendLine($"{Tabs(2)}ServerTcp tcp = (ServerTcp)reliable;");
		hybrid.AppendLine($"{Tabs(2)}ServerUdp udp = new(port);");
		hybrid.AppendLine($"{Tabs(2)}tcp.ConnAdded = (client, id) => udp.TcpConn((System.Net.IPEndPoint)client.Client.RemoteEndPoint, id);");
		if (GetInternalCorrelation(types, "Disconnect") != "") {
			hybrid.AppendLine($"{Tabs(2)}tcp.RegisterPacketProcessor({packetTypeFullName}.{GetInternalCorrelation(types, "Disconnect")}, ({types.packetident.First().ns}.{types.correlates.First().qualName} _, ushort id) => {{");
			hybrid.AppendLine($"{Tabs(3)}udp.TcpDisc(id);");
			hybrid.AppendLine($"{Tabs(2)}}});");
		}
		hybrid.AppendLine($"{Tabs(2)}return udp;");
		hybrid.AppendLine($"{Tabs()}}}");
		hybrid.AppendLine("}");
		prodContext.AddSource("network/factory-tcpudp.g.cs", hybrid.ToString());
	}

	static void GenerateHostLayer(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder layer = new();
		layer.AppendLine(CodeBaseIncludes);
		layer.AppendLine($"using {types.packetident.First().ns};");
		layer.AppendLine("using tairasoul.unity.common.networking.clients;");
		layer.AppendLine("using tairasoul.unity.common.networking.servers;");
		layer.AppendLine("using tairasoul.unity.common.networking.factories;");
		layer.AppendLine("namespace tairasoul.unity.common.networking.layer;");
		layer.AppendLine("partial class HostBasedP2P : INetworkLayer {");
		layer.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		layer.AppendLine($"{Tabs()}public HostBasedP2P(bool isHost, ITransportFactory factory, int reliablePort, int unreliablePort, string username) {{");
		layer.AppendLine($"{Tabs(2)}this.isHost = isHost;");
		layer.AppendLine($"{Tabs(2)}this.username = username;");
		layer.AppendLine($"{Tabs(2)}if (isHost) {{");
		layer.AppendLine($"{Tabs(3)}IServer reliableServer = factory.CreateReliableServer(reliablePort);");
		layer.AppendLine($"{Tabs(3)}IServer unreliableServer = factory.CreateUnreliableServer(reliableServer, unreliablePort);");
		layer.AppendLine($"{Tabs(3)}servers = new()");
		layer.AppendLine($"{Tabs(3)}{{");
		layer.AppendLine($"{Tabs(4)}reliable = reliableServer,");
		layer.AppendLine($"{Tabs(4)}unreliable = unreliableServer");
		layer.AppendLine($"{Tabs(3)}}};");
		layer.AppendLine($"{Tabs(4)}playerId = 0;");
		if (GetInternalCorrelation(types, "Connect") != "" && GetInternalCorrelation(types, "PlayerConnected") != null)
		{
			layer.AppendLine($"{Tabs(4)}OnPacket({packetTypeFullName}.{GetInternalCorrelation(types, "Connect")}, (InternalConnectPacket conn, ushort id) =>");
			layer.AppendLine($"{Tabs(4)}{{");
			layer.AppendLine($"{Tabs(5)}ActionQueue.Enqueue(() =>");
			layer.AppendLine($"{Tabs(5)}{{");
			layer.AppendLine($"{Tabs(6)}InternalPlayerConnectedPacket c1 = new(0, username);");
			layer.AppendLine($"{Tabs(6)}servers.reliable.Relay(c1, id);");
			layer.AppendLine($"{Tabs(6)}foreach (var player in players)");
			layer.AppendLine($"{Tabs(6)}{{");
			layer.AppendLine($"{Tabs(7)}InternalPlayerConnectedPacket c = new(player.Key, player.Value);");
			layer.AppendLine($"{Tabs(7)}servers.reliable.Relay(c, id);");
			layer.AppendLine($"{Tabs(6)}}}");
			layer.AppendLine($"{Tabs(6)}if (servers.unreliable is ServerUdp udp)");
			layer.AppendLine($"{Tabs(7)}udp.players[id].addr.Port = conn.udpPort;");
			layer.AppendLine($"{Tabs(6)}InternalPlayerConnectedPacket connected = new(id, conn.username);");
			layer.AppendLine($"{Tabs(6)}servers.reliable.RelayExcept(connected, id);");
			layer.AppendLine($"{Tabs(6)}written = true;");
			layer.AppendLine($"{Tabs(5)}}});");
			layer.AppendLine($"{Tabs(4)}}});");
		}
		layer.AppendLine($"{Tabs(2)}}}");
		layer.AppendLine($"{Tabs(2)}else {{");
		layer.AppendLine($"{Tabs(3)}clients = new();");
		layer.AppendLine($"{Tabs(3)}this.unreliablePort = unreliablePort;");
		layer.AppendLine($"{Tabs(3)}this.transportFactory = factory;");
		layer.AppendLine($"{Tabs(2)}}}");
		layer.AppendLine($"{Tabs()}}}");
		layer.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		layer.AppendLine($"{Tabs()}public void ConnectTo(string host, int port) {{");
		layer.AppendLine($"{Tabs(2)}if (isHost) throw new InvalidOperationException(\"Cannot connect to another server as a server!\");");
		layer.AppendLine($"{Tabs(2)}clients.reliable = transportFactory.CreateReliableClient(host, port);");
		layer.AppendLine($"{Tabs(2)}clients.unreliable = transportFactory.CreateUnreliableClient(clients.reliable, host, port, unreliablePort);");
		if (GetInternalCorrelation(types, "IdRelay") != "") {
			layer.AppendLine($"{Tabs(2)}OnPacket({packetTypeFullName}.{GetInternalCorrelation(types, "IdRelay")}, (InternalIdRelayPacket idRelay, ushort _) => {{");
			layer.AppendLine($"{Tabs(3)}playerId = idRelay.playerId;");
			layer.AppendLine($"{Tabs(2)}}});");
		}
		if (GetInternalCorrelation(types, "PlayerConnected") != "") {
			layer.AppendLine($"{Tabs(2)}OnPacket({packetTypeFullName}.{GetInternalCorrelation(types, "PlayerConnected")}, (InternalPlayerConnectedPacket playerConn, ushort _) => {{");
			layer.AppendLine($"{Tabs(3)}players[playerConn.playerId] = playerConn.username;");
			layer.AppendLine($"{Tabs(2)}}});");
		}
		layer.AppendLine($"{Tabs()}}}");
		layer.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		layer.AppendLine($"{Tabs()}public void checkIfConnectionSent() {{");
		if (GetInternalCorrelation(types, "Connect") != "")
		{
			layer.AppendLine($"{Tabs(2)}if (isHost) return;");
			layer.AppendLine($"{Tabs(2)}if (sentConnect) return;");
			layer.AppendLine($"{Tabs(2)}sentConnect = true;");
			layer.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
			layer.AppendLine($"{Tabs(3)}InternalConnectPacket connect = new(unreliablePort, username);");
			layer.AppendLine($"{Tabs(3)}clients.reliable.SendPacket(connect);");
			layer.AppendLine($"{Tabs(3)}clients.reliable.Flush();");
			layer.AppendLine($"{Tabs(2)}}});");
		}
		layer.AppendLine($"{Tabs()}}}");
		layer.AppendLine("}");
		prodContext.AddSource("network/layers.host.g.cs", layer.ToString());
	}

	public static void GenerateInternalStructs(SourceProductionContext prodContext, Types types) {
		StringBuilder sb = new();
		sb.AppendLine("using System.CodeDom.Compiler;");
		sb.AppendLine("using System;");
		sb.AppendLine("using tairasoul.unity.common.networking.interfaces;");
		sb.AppendLine("namespace tairasoul.unity.common.networking.gentypes;");
		if (GetInternalCorrelation(types, "Connect") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record struct InternalConnectPacket(int udpPort, string username) : IPacket;");
		}
		if (GetInternalCorrelation(types, "IdRelay") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record struct InternalIdRelayPacket(ushort playerId) : IPacket;");
		}
		if (GetInternalCorrelation(types, "PlayerConnected") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record struct InternalPlayerConnectedPacket(ushort playerId, string username) : IPacket;");
		}
		prodContext.AddSource("network/internals.structs.g.cs", sb.ToString());
	}
}