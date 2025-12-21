using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static tairasoul.unity.common.sourcegen.networking.util.StringUtil;
using sdi = System.Collections.Immutable.ImmutableArray<tairasoul.unity.common.sourcegen.networking.SymbolData>;

namespace tairasoul.unity.common.sourcegen.networking;

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
record Types(sdi correlates, sdi icorrelates, sdi relay, sdi reliability, sdi packetident, sdi iur, sdi irr, sdi iuhw, sdi irhw, sdi irg, sdi isr);

class NetGen {
	const string CodeBaseIncludes = 
	"""
	using tairasoul.unity.common.bits;
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
				"PGEN002",
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
		GenerateClientInterface(context, types);
		GenerateServerInterface(context, types);
		GenerateLayerInterface(context, types);
		GenerateTcpUdpHybridFactory(context, types);
		GenerateTcpClient(context, types);
		GenerateUdpClient(context, types);
		GenerateTcpServer(context, types);
		GenerateUdpServer(context, types);
		GenerateHostLayer(context, types);
		GenerateUnreliableReads(context, types);
		GenerateReliableReads(context, types);
		GenerateUnreliableHeaderWrites(context, types);
		GenerateReliableHeaderWrites(context, types);
		GenerateReliabilityGet(context, types);
		GenerateServerRelay(context, types);
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

	static void GenerateClientInterface(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder inter = new();
		inter.AppendLine(CodeBaseIncludes);
		inter.AppendLine($"using {types.packetident.First().ns};");
		inter.AppendLine("namespace tairasoul.unity.common.networking.clients;");
		inter.AppendLine("partial interface IClient {");
		inter.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		inter.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T> processor) where T : IPacket;");
		inter.AppendLine("}");
		prodContext.AddSource("network/clients.interface.g.cs", inter.ToString());
	}

	static void GenerateServerInterface(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder inter = new();
		inter.AppendLine(CodeBaseIncludes);
		inter.AppendLine($"using {types.packetident.First().ns};");
		inter.AppendLine("namespace tairasoul.unity.common.networking.servers;");
		inter.AppendLine("partial interface IServer {");
		inter.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		inter.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T, ushort> processor) where T : IPacket;");
		inter.AppendLine("}");
		prodContext.AddSource("network/servers.interface.g.cs", inter.ToString());
	}

	static void GenerateLayerInterface(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder inter = new();
		inter.AppendLine(CodeBaseIncludes);
		inter.AppendLine($"using {types.packetident.First().ns};");
		inter.AppendLine("namespace tairasoul.unity.common.networking.layer;");
		inter.AppendLine("partial interface INetworkLayer {");
		inter.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		inter.AppendLine($"{Tabs()}public void OnPacket<T>({packetTypeFullName} type, Action<T, ushort> processor) where T : IPacket;");
		inter.AppendLine("}");
		prodContext.AddSource("network/layer.interface.g.cs", inter.ToString());
	}

	static void GenerateTcpClient(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder client = new();
		client.AppendLine(CodeBaseIncludes);
		client.AppendLine("using System.Threading;");
		client.AppendLine($"using {types.packetident.First().ns};");
		client.AppendLine("namespace tairasoul.unity.common.networking.clients;");
		client.AppendLine("partial class ClientTcp : IClient");
		client.AppendLine("{");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}Dictionary<{packetTypeFullName}, Action<object>[]> processors = [];");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T> processor) where T : IPacket {{");
		client.AppendLine($"{Tabs(2)}processors[type] = [..processors.ContainsKey(type) ? processors[type] : [], obj => processor((T)obj)];");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void Flush() {{");
		client.AppendLine($"{Tabs()}if (!needsFlush) return;");
		client.AppendLine($"{Tabs()}needsFlush = false;");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "BatchEnd") != "") {
			client.AppendLine($"{Tabs(3)}bitWriter.Write({packetTypeFullName}.{GetInternalCorrelation(types, "BatchEnd")});");
		}
		client.AppendLine($"{Tabs(3)}bitWriter.Flush();");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}public void Disconnect() {{");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "Disconnect") != "") {
			client.AppendLine($"{Tabs(3)}bitWriter.Write({packetTypeFullName}.{GetInternalCorrelation(types, "Disconnect")});");
		}
		client.AppendLine($"{Tabs(3)}bitWriter.Flush();");
		client.AppendLine($"{Tabs(3)}client.Close();");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine("}");
		prodContext.AddSource("network/clients.tcp.g.cs", client.ToString());
	}

	static void GenerateUdpClient(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder client = new();
		client.AppendLine(CodeBaseIncludes);
		client.AppendLine($"using {types.packetident.First().ns};");
		client.AppendLine("namespace tairasoul.unity.common.networking.clients;");
		client.AppendLine("partial class ClientUdp : IClient");
		client.AppendLine("{");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}Dictionary<{packetTypeFullName}, Action<object>[]> processors = [];");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T> processor) where T : IPacket {{");
		client.AppendLine($"{Tabs(2)}processors[type] = [..processors.ContainsKey(type) ? processors[type] : [], obj => processor((T)obj)];");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		client.AppendLine($"{Tabs()}public void Flush() {{");
		client.AppendLine($"{Tabs()}if (!needsFlush) return;");
		client.AppendLine($"{Tabs()}needsFlush = false;");
		client.AppendLine($"{Tabs(2)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "BatchEnd") != "") {
			client.AppendLine($"{Tabs(3)}serializedWriter.Write({packetTypeFullName}.{GetInternalCorrelation(types, "BatchEnd")});");
		}
		client.AppendLine($"{Tabs(3)}serializedWriter.Flush();");
		client.AppendLine($"{Tabs(3)}byte[] bytes = serialization.ToArray();");
		client.AppendLine($"{Tabs(3)}client.Send(bytes, bytes.Length, host, port);");
		client.AppendLine($"{Tabs(3)}serialization.SetLength(0);");
		client.AppendLine($"{Tabs(2)}}});");
		client.AppendLine($"{Tabs()}}}");
		client.AppendLine("}");
		prodContext.AddSource("network/clients.udp.g.cs", client.ToString());
	}

	static void GenerateTcpServer(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder server = new();
		server.AppendLine(CodeBaseIncludes);
		server.AppendLine($"using {types.packetident.First().ns};");
		server.AppendLine("namespace tairasoul.unity.common.networking.servers;");
		server.AppendLine("partial class ServerTcp : IServer {");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}Dictionary<{packetTypeFullName}, Action<object, ushort>[]> processors = [];");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T, ushort> processor) where T : IPacket {{");
		server.AppendLine($"{Tabs(2)}processors[type] = [..processors.ContainsKey(type) ? processors[type] : [], (obj, id) => processor((T)obj, id)];");
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void Flush() {{");
		server.AppendLine($"{Tabs(2)}foreach (var conn in players.Values) {{");
		server.AppendLine($"{Tabs(3)}if (!conn.needsFlush) continue;");
		server.AppendLine($"{Tabs(3)}conn.needsFlush = false;");
		server.AppendLine($"{Tabs(3)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "BatchEnd") != "") {
			server.AppendLine($"{Tabs(4)}conn.writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "BatchEnd")});");
		}
		server.AppendLine($"{Tabs(4)}conn.writer.Flush();");
		server.AppendLine($"{Tabs(3)}}});");
		server.AppendLine($"{Tabs(2)}}}");
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine("}");
		prodContext.AddSource("network/servers.tcp.g.cs", server.ToString());
	}

	static void GenerateUdpServer(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		StringBuilder server = new();
		server.AppendLine(CodeBaseIncludes);
		server.AppendLine($"using {types.packetident.First().ns};");
		server.AppendLine("namespace tairasoul.unity.common.networking.servers;");
		server.AppendLine("partial class ServerUdp : IServer {");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}Dictionary<{packetTypeFullName}, Action<object, ushort>[]> processors = [];");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void RegisterPacketProcessor<T>({packetTypeFullName} type, Action<T, ushort> processor) where T : IPacket {{");
		server.AppendLine($"{Tabs(2)}processors[type] = [..processors.ContainsKey(type) ? processors[type] : [], (obj, id) => processor((T)obj, id)];");
		server.AppendLine($"{Tabs()}}}");
		server.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
		server.AppendLine($"{Tabs()}public void Flush() {{");
		server.AppendLine($"{Tabs(2)}foreach (var conn in players.Values) {{");
		server.AppendLine($"{Tabs(3)}if (!conn.requiresFlush) continue;");
		server.AppendLine($"{Tabs(3)}conn.requiresFlush = false;");
		server.AppendLine($"{Tabs(3)}ActionQueue.Enqueue(() => {{");
		if (GetInternalCorrelation(types, "BatchEnd") != "") {
			server.AppendLine($"{Tabs(4)}conn.writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "BatchEnd")});");
		}
		server.AppendLine($"{Tabs(4)}conn.writer.Flush();");
		server.AppendLine($"{Tabs(4)}byte[] bytes = conn.writeMem.ToArray();");
		server.AppendLine($"{Tabs(4)}client.Send(bytes, bytes.Length, conn.addr);");
		server.AppendLine($"{Tabs(4)}conn.writeMem.SetLength(0);");
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
			hybrid.AppendLine($"{Tabs(2)}tcp.RegisterPacketProcessor({packetTypeFullName}.{GetInternalCorrelation(types, "Disconnect")}, ({types.correlates.First().qualName} _, ushort id) => {{");
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
		layer.AppendLine($"{Tabs(4)}playerId = 1;");
		if (GetInternalCorrelation(types, "Connect") != "" && GetInternalCorrelation(types, "PlayerConnected") != null)
		{
			layer.AppendLine($"{Tabs(4)}OnPacket({packetTypeFullName}.{GetInternalCorrelation(types, "Connect")}, (InternalConnectPacket conn, ushort id) =>");
			layer.AppendLine($"{Tabs(4)}{{");
			layer.AppendLine($"{Tabs(5)}ActionQueue.Enqueue(() =>");
			layer.AppendLine($"{Tabs(5)}{{");
			layer.AppendLine($"{Tabs(6)}InternalPlayerConnectedPacket c1 = new(1, username);");
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
		layer.AppendLine($"{Tabs()}public void OnPacket<T>({packetTypeFullName} {packetTypeFullName.Replace(".", "_")}, Action<T, ushort> listener) where T : IPacket {{");
		layer.AppendLine($"{Tabs(2)}if (isHost) {{");
		layer.AppendLine($"{Tabs(3)}servers.reliable.RegisterPacketProcessor({packetTypeFullName.Replace(".", "_")}, listener);");
		layer.AppendLine($"{Tabs(3)}servers.unreliable.RegisterPacketProcessor({packetTypeFullName.Replace(".", "_")}, listener);");
		layer.AppendLine($"{Tabs(2)}}}");
		layer.AppendLine($"{Tabs(2)}else {{");
		layer.AppendLine($"{Tabs(3)}clients.reliable.RegisterPacketProcessor<T>({packetTypeFullName.Replace(".", "_")}, packet => listener(packet, 0));");
		layer.AppendLine($"{Tabs(3)}clients.unreliable.RegisterPacketProcessor<T>({packetTypeFullName.Replace(".", "_")}, packet => listener(packet, 0));");
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
		sb.AppendLine("using tairasoul.unity.common.bits;");
		sb.AppendLine("using tairasoul.unity.common.attributes.bits;");
		sb.AppendLine("namespace tairasoul.unity.common.networking.gentypes;");
		if (GetInternalCorrelation(types, "Connect") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record InternalConnectPacket(int udpPort, string username) : IPacket;");
		}
		if (GetInternalCorrelation(types, "IdRelay") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record InternalIdRelayPacket([property: ItemBitSize(12)] ushort playerId) : IPacket;");
		}
		if (GetInternalCorrelation(types, "PlayerConnected") != "")
		{
			sb.AppendLine($"[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"record InternalPlayerConnectedPacket([property: ItemBitSize(12)] ushort playerId, string username) : IPacket;");
		}
		prodContext.AddSource("network/internals.structs.g.cs", sb.ToString());
	}

	static void GenerateUnreliableReads(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		foreach (var ur in types.iur) {
			bool skip = types.irr.Any((v) => v.qualName == ur.qualName && v.ns == ur.ns);
			if (skip) continue;
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {ur.ns};");
			sb.AppendLine($"partial class {ur.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}public async Task<({packetTypeFullName} packetType, IPacket data)?> ReadPacket(BitReaderAsync bitReader) {{");
			sb.AppendLine($"{Tabs(2)}{packetTypeFullName} _autogenPacketType = await bitReader.Read<{packetTypeFullName}>();");
			sb.AppendLine($"{Tabs(2)}switch (_autogenPacketType) {{");
			foreach (var sym in types.correlates) {
				if (!IsUnreliable(types, sym)) continue;
				if (!sym.arguments.First().StartsWith(packetTypeUsed) && !sym.arguments.First().StartsWith(packetTypeFullName)) {
					prodContext.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
						"PGEN001",
						"Enum mismatch",
						$"Correlation {sym.arguments.First()} is not of packet {packetTypeUsed}",
						"Generation",
						DiagnosticSeverity.Error,
						true
					),
					Location.None));
					continue;
				}
				sb.AppendLine($"{Tabs(3)}case {sym.arguments.First()}:");
				sb.AppendLine($"{Tabs(4)}{sym.qualName} {sym.qualName.Replace(".", "_")} = await bitReader.Read<{sym.qualName}>();");
				sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, {sym.qualName.Replace(".", "_")});");
			}
			sb.AppendLine($"{Tabs(2)}}}");
			sb.AppendLine($"{Tabs(2)}return null;");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{ur.qualName}-unreliable-reads.g.cs", sb.ToString());
		}
	}

	static void GenerateReliableReads(SourceProductionContext prodContext, Types types) {
		string packetTypeUsed = types.packetident.First().name;
		string packetTypeFullName = types.packetident.First().qualName;
		foreach (var ur in types.irr) {
			bool includeUnreliable = types.iur.Any((v) => v.qualName == ur.qualName && v.ns == ur.ns);
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {ur.ns};");
			sb.AppendLine($"partial class {ur.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}public async Task<({packetTypeFullName} packetType, IPacket data)?> ReadPacket(BitReaderAsync bitReader) {{");
			sb.AppendLine($"{Tabs(2)}{packetTypeFullName} _autogenPacketType = await bitReader.Read<{packetTypeFullName}>();");
			sb.AppendLine($"{Tabs(2)}switch (_autogenPacketType) {{");
			foreach (var sym in types.correlates) {
				if (!IsReliable(types, sym) && !includeUnreliable) continue;
				if (!sym.arguments.First().StartsWith(packetTypeUsed) && !sym.arguments.First().StartsWith(packetTypeFullName)) {
					prodContext.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
						"PGEN001",
						"Enum mismatch",
						$"Correlation {sym.arguments.First()} is not of packet {packetTypeUsed}",
						"Generation",
						DiagnosticSeverity.Error,
						true
					),
					Location.None));
					continue;
				}
				sb.AppendLine($"{Tabs(3)}case {sym.arguments.First()}:");
				sb.AppendLine($"{Tabs(4)}{sym.qualName} {sym.qualName.Replace(".", "_")} = await bitReader.Read<{sym.qualName}>();");
				sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, {sym.qualName.Replace(".", "_")});");
			}
			foreach (var ic in types.icorrelates) {
				if (!ic.name.StartsWith(packetTypeUsed) && !ic.name.StartsWith(packetTypeFullName)) {
					prodContext.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
						"PGEN001",
						"Enum mismatch",
						$"Internal correlation {ic.name} is not of packet {packetTypeUsed}",
						"Generation",
						DiagnosticSeverity.Error,
						true
					),
					Location.None));
					continue;
				}
				string internalCorrelate = ic.arguments.First();
				InternalCorrelationType correlType;
				if (internalCorrelate.EndsWith("PacketBatchEnd"))
					correlType = InternalCorrelationType.PacketBatchEnd;
				else if (internalCorrelate.EndsWith("IdRelay"))
					correlType = InternalCorrelationType.IdRelay;
				else if (internalCorrelate.EndsWith("Connect"))
					correlType = InternalCorrelationType.Connect;
				else if (internalCorrelate.EndsWith("Disconnect"))
					correlType = InternalCorrelationType.Disconnect;
				else if (internalCorrelate.EndsWith("PlayerConnected"))
					correlType = InternalCorrelationType.PlayerConnected;
				else
					correlType = InternalCorrelationType.None;
				if (correlType == InternalCorrelationType.None) continue;
				switch (correlType) {
					case InternalCorrelationType.PacketBatchEnd:
						sb.AppendLine($"{Tabs(3)}case {packetTypeFullName}.{ic.qualName}:");
						sb.AppendLine($"{Tabs(4)}bitReader.Reset();");
						sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, null);");
						break;
					case InternalCorrelationType.IdRelay:
						sb.AppendLine($"{Tabs(3)}case {packetTypeFullName}.{ic.qualName}:");
						sb.AppendLine($"{Tabs(4)}InternalIdRelayPacket internalIdRelay = await bitReader.Read<InternalIdRelayPacket>();");
						sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, internalIdRelay);");
						break;
					case InternalCorrelationType.Connect:
						sb.AppendLine($"{Tabs(3)}case {packetTypeFullName}.{ic.qualName}:");
						sb.AppendLine($"{Tabs(4)}InternalConnectPacket internalConnect = await bitReader.Read<InternalConnectPacket>();");
						sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, internalConnect);");
						break;
					case InternalCorrelationType.PlayerConnected:
						sb.AppendLine($"{Tabs(3)}case {packetTypeFullName}.{ic.qualName}:");
						sb.AppendLine($"{Tabs(4)}InternalPlayerConnectedPacket internalPlayerConnect = await bitReader.Read<InternalPlayerConnectedPacket>();");
						sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, internalPlayerConnect);");
						break;
					case InternalCorrelationType.Disconnect:
						sb.AppendLine($"{Tabs(3)}case {packetTypeFullName}.{ic.qualName}:");
						sb.AppendLine($"{Tabs(4)}return (_autogenPacketType, null);");
						break;
				}
			}
			sb.AppendLine($"{Tabs(2)}}}");
			sb.AppendLine($"{Tabs(2)}return null;");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{ur.qualName}-reliable-reads.g.cs", sb.ToString());
		}
	}

	static void GenerateUnreliableHeaderWrites(SourceProductionContext prodContext, Types types) {
		foreach (var uh in types.iuhw) {
			bool skip = types.irhw.Any((v) => v.qualName == uh.qualName && v.ns == uh.ns);
			if (skip) continue;
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {uh.ns};");
			sb.AppendLine($"partial class {uh.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}public bool WritePacketHeader<T>(T packet, BitWriter writer) where T : IPacket {{");
			foreach (var correl in types.correlates) {
				if (!IsUnreliable(types, correl)) continue;
				sb.AppendLine($"{Tabs(2)}if (packet is {correl.qualName}) {{");
				sb.AppendLine($"{Tabs(3)}writer.Write({correl.arguments.First()});");
				sb.AppendLine($"{Tabs(3)}return true;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			sb.AppendLine($"{Tabs(2)}return false;");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{uh.qualName}-unreliable-headers.g.cs", sb.ToString());
		}
	}

	static void GenerateReliableHeaderWrites(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		foreach (var uh in types.irhw) {
			bool includeUnreliable = types.iuhw.Any((v) => v.qualName == uh.qualName && v.ns == uh.ns);
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {uh.ns};");
			sb.AppendLine($"partial class {uh.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}public bool WritePacketHeader<T>(T packet, BitWriter writer) where T : IPacket {{");
			if (GetInternalCorrelation(types, "Connect") != "")
			{
				sb.AppendLine($"{Tabs(2)}if (packet is InternalConnectPacket) {{");
				sb.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "Connect")});");
				sb.AppendLine($"{Tabs(3)}return true;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			if (GetInternalCorrelation(types, "PlayerConnected") != "") {
				sb.AppendLine($"{Tabs(2)}if (packet is InternalPlayerConnectedPacket) {{");
				sb.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "PlayerConnected")});");
				sb.AppendLine($"{Tabs(3)}return true;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			if (GetInternalCorrelation(types, "IdRelay") != "") {
				sb.AppendLine($"{Tabs(2)}if (packet is InternalIdRelayPacket) {{");
				sb.AppendLine($"{Tabs(3)}writer.Write({packetTypeFullName}.{GetInternalCorrelation(types, "IdRelay")});");
				sb.AppendLine($"{Tabs(3)}return true;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			foreach (var correl in types.correlates) {
				if (!IsReliable(types, correl) && !includeUnreliable) continue;
				sb.AppendLine($"{Tabs(2)}if (packet is {correl.qualName}) {{");
				sb.AppendLine($"{Tabs(3)}writer.Write({correl.arguments.First()});");
				sb.AppendLine($"{Tabs(3)}return true;");
				sb.AppendLine($"{Tabs(2)}}}");
			}
			sb.AppendLine($"{Tabs(2)}return false;");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{uh.qualName}-reliable-headers.g.cs", sb.ToString());
		}
	}

	static void GenerateReliabilityGet(SourceProductionContext prodContext, Types types) {
		string packetTypeFullName = types.packetident.First().qualName;
		foreach (var rg in types.irg) {
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {rg.ns};");
			sb.AppendLine($"partial class {rg.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}public PacketReliability GetPacketReliability<T>(T data) where T : IPacket {{");
			if (GetInternalCorrelation(types, "Connect") != "") {
				sb.AppendLine($"{Tabs(2)}if (data is InternalConnectPacket) return PacketReliability.Reliable;");
			}
			if (GetInternalCorrelation(types, "PlayerConnected") != "") {
				sb.AppendLine($"{Tabs(2)}if (data is InternalPlayerConnectedPacket) return PacketReliability.Reliable;");
			}
			if (GetInternalCorrelation(types, "IdRelay") != "") {
				sb.AppendLine($"{Tabs(2)}if (data is InternalIdRelayPacket) return PacketReliability.Reliable;");
			}
			foreach (var correl in types.correlates) {
				if (IsReliable(types, correl) && !IsUnreliable(types, correl))
					sb.AppendLine($"{Tabs(2)}if (data is {correl.qualName}) return PacketReliability.Reliable;");
				else if (IsUnreliable(types, correl) && !IsReliable(types, correl))
					sb.AppendLine($"{Tabs(2)}if (data is {correl.qualName}) return PacketReliability.Unreliable;");
				else if (IsReliable(types, correl) && IsUnreliable(types, correl))
					sb.AppendLine($"{Tabs(2)}if (data is {correl.qualName}) throw new ArgumentException(\"{correl.qualName} is both a reliable and unreliable packet. This has to be manually specified.\", nameof(data));");
			}
			sb.AppendLine($"{Tabs(2)}throw new Exception($\"{{data}} has no valid reliabilites marked.\");");
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{rg.qualName}-reliability-get.g.cs", sb.ToString());
		}
	}

	static void GenerateServerRelay(SourceProductionContext prodContext, Types types) {
		foreach (var sr in types.isr) {
			StringBuilder sb = new();
			sb.AppendLine(CodeBaseIncludes);
			sb.AppendLine($"using {types.packetident.First().ns};");
			sb.AppendLine($"namespace {sr.ns};");
			sb.AppendLine($"partial class {sr.qualName} {{");
			sb.AppendLine($"{Tabs()}[GeneratedCode({GeneratedCodeData})]");
			sb.AppendLine($"{Tabs()}void DoServerRelay<T>(T packet, ushort id) where T : IPacket {{");
			foreach (var sym in types.correlates) {
				foreach (var rel in types.relay) {
					if (rel.name.Trim() == sym.arguments.First().Trim()) {
						switch (rel.arguments.First().Trim()) {
							case "ServerRelayType.RelayExceptSender":
								sb.AppendLine($"{Tabs(2)}if (packet is {sym.qualName} {sym.qualName.Replace(".", "_")}) {{");
								sb.AppendLine($"{Tabs(3)}RelayExcept({sym.qualName.Replace(".", "_")}, id);");
								sb.AppendLine($"{Tabs(3)}return;");
								sb.AppendLine($"{Tabs(2)}}}");
								break;
						}
					}
				}
			}
			sb.AppendLine($"{Tabs()}}}");
			sb.AppendLine("}");
			prodContext.AddSource($"implementations/{sr.qualName}-server-relay.g.cs", sb.ToString());
		}
	}
}