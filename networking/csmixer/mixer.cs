using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Cil;
using tairasoul.unity.common.csmixer.attributes;
using tairasoul.unity.common.format.attributes;
using tairasoul.unity.common.hashing;
using tairasoul.unity.common.networking.attributes.packets;
using tairasoul.unity.common.networking.interfaces;
using EmptyBody = tairasoul.unity.common.networking.empty.EmptyBody;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace tairasoul.unity.common.networking.csmixer;

[ScannedMix]
class NetworkingMixer {
	const string ServerInterface = "tairasoul.unity.common.networking.servers.IServer";
	const string ClientInterface = "tairasoul.unity.common.networking.clients.IClient";
	const string PacketIdentifier = "tairasoul.unity.common.networking.attributes.packets.PacketTypeIdentifier";
	const string CorrelatesTo = "tairasoul.unity.common.networking.attributes.packets.CorrelatesTo";
	const string CorrelatesToInternal = "tairasoul.unity.common.networking.attributes.packets.CorrelatesToInternal";
	const string FormatMethodsFor = "tairasoul.unity.common.format.attributes.FormatMethodsForAttribute";
	const string PacketInterfaceName = "tairasoul.unity.common.networking.interfaces.IPacket";
	const string PacketHandlerName = "tairasoul.unity.common.networking.attributes.handling.PacketHandlerAttribute";
	const string ExplicitHandler = "tairasoul.unity.common.networking.attributes.handling.ExplicitHandlerAttribute";
	const string EmptyBodyFullName = "tairasoul.unity.common.networking.empty.EmptyBody";
	const string BlanketInterfaceAttribute = "tairasoul.unity.common.networking.attributes.handling.BlanketInterfaceAttribute";
	const string SpecializeBlanketAttribute = "tairasoul.unity.common.networking.attributes.handling.SpecializeBlanketAttribute";

	public static async Task<object?> ConvertGeneric<T>(Task<T> task)
	{
		T res = await task;
		return res!;
	}
	
	[Scanner]
	static void Scan(AssemblyDefinition thisAsm, IEnumerable<AssemblyDefinition> allAsm)
	{
		TypeDefinition[] allTypes = [.. thisAsm.MainModule.Types, .. allAsm.SelectMany((v) => v.MainModule.Types)];
		var empty = ImplementEmpty(thisAsm, allTypes);
		allTypes = [empty, .. allTypes];
		AssemblyDefinition[] assemblies = [thisAsm, .. allAsm];
		Array.Sort(assemblies, (x, y) => string.Compare(x.FullName, y.FullName));
		allTypes = [empty, .. assemblies.Select((v) => v.MainModule.Types).SelectMany(v => v)];
		var mapping = GetIdMapping(allTypes).OrderBy((v) => v.Item2).ToArray();
		ConcurrentBag<TypeDefinition> clientTypes = [];
		ConcurrentBag<TypeDefinition> serverTypes = [];
		Parallel.ForEach(allTypes, (type) =>
		{
			foreach (var interf in type.Interfaces)
			{
				if (interf.InterfaceType.FullName == ServerInterface)
					serverTypes.Add(type);
				else if (interf.InterfaceType.FullName == ClientInterface)
					clientTypes.Add(type);
			}
		});
		ImplementRead(thisAsm, clientTypes, serverTypes, mapping);
		ImplementRouting(allTypes, clientTypes, serverTypes, mapping);
		ImplementPacketCheck(clientTypes, serverTypes, mapping);
	}

	static void ImplementPacketCheck(IEnumerable<TypeDefinition> clientTypes, IEnumerable<TypeDefinition> serverTypes, IEnumerable<(FieldDefinition, uint, TypeDefinition, TypeDefinition)> idMapping) {
		void emitType(ILProcessor il, TypeReference reference) {
			var def = il.Body.Method.Module;
			il.Emit(OpCodes.Ldtoken, def.ImportReference(reference));
			il.Emit(OpCodes.Call, def.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle")));
		}
		Instruction emitGen(ILProcessor il, GenericParameter reference) {
			var def = il.Body.Method.Module;
			Instruction i = il.Create(OpCodes.Ldtoken, def.ImportReference(reference));
			il.Append(i);
			il.Emit(OpCodes.Call, def.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle")));
			return i;
		}
		void implement(TypeDefinition implementor) {
			var packetMethod = implementor.Methods.Single((v) => v.Name == "GetPacketId");
			var packetBody = packetMethod.Body;
			var il = packetBody.GetILProcessor();
			var genParam = packetMethod.GenericParameters.First();
			var typeEq = packetMethod.Module.ImportReference(typeof(Type).GetMethod("Equals", [typeof(Type)]));
			packetBody.Instructions.Clear();
			List<(Instruction, Instruction)> brFalse = [];
			foreach (var mapping in idMapping) {
				if (mapping.Item3.FullName == EmptyBodyFullName) continue;
				Instruction start = emitGen(il, genParam);
				emitType(il, mapping.Item3);
				Instruction instr = il.Create(OpCodes.Call, typeEq);
				il.Append(instr);
				brFalse.Add((start, instr));
				il.Emit(OpCodes.Ldc_I4, (int)mapping.Item2);
				il.Emit(OpCodes.Ret);
			}
			Instruction endInstr = il.Create(OpCodes.Ldc_I4, unchecked((int)uint.MaxValue));
			il.Append(endInstr);
			il.Emit(OpCodes.Ret);
			for (int i = 0; i < brFalse.Count - 1; i++) {
				var brFrom = brFalse[i];
				var brTo = brFalse[i + 1];
				il.InsertAfter(brFrom.Item2, il.Create(OpCodes.Brfalse, brTo.Item1));
			}
			var last = brFalse.Last();
			il.InsertAfter(last.Item2, il.Create(OpCodes.Brfalse, endInstr));
		}
		void implementByHeader(TypeDefinition implementor) {
			var packetMethod = implementor.Methods.Single((v) => v.Name == "GetPacketByHeader");
			var packetBody = packetMethod.Body;
			var il = packetBody.GetILProcessor();
			packetBody.Instructions.Clear();
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Call, packetMethod.Module.ImportReference(typeof(object).GetMethod("GetType")));
			var typeEq = packetMethod.Module.ImportReference(typeof(Type).GetMethod("Equals", [typeof(Type)]));
			VariableDefinition variable = new(packetMethod.Module.ImportReference(typeof(Type)));
			packetBody.Variables.Add(variable);
			il.Emit(OpCodes.Stloc, variable);
			List<(Instruction, Instruction)> brFalse = [];
			List<Instruction> Bne = [];
			foreach (var mapping in idMapping)
			{
				if (mapping.Item3.FullName != EmptyBodyFullName) continue;
				Instruction start = il.Create(OpCodes.Ldloc, variable);
				il.Append(start);
				emitType(il, mapping.Item1.DeclaringType);
				Instruction instr = il.Create(OpCodes.Call, typeEq);
				il.Append(instr);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Unbox_Any, packetMethod.Module.TypeSystem.UInt32);
				Instruction beq = il.Create(OpCodes.Ldsfld, mapping.Item1);
				il.Append(beq);
				brFalse.Add((start, instr));
				Bne.Add(beq);
				il.Emit(OpCodes.Ldc_I4, (int)mapping.Item2);
				il.Emit(OpCodes.Ret);
			}
			Instruction endInstr = il.Create(OpCodes.Ldc_I4, unchecked((int)uint.MaxValue));
			il.Append(endInstr);
			il.Emit(OpCodes.Ret);
			for (int i = 0; i < Bne.Count - 1; i++) {
				var beq = Bne[i];
				var next = brFalse[i + 1];
				il.InsertAfter(beq, il.Create(OpCodes.Bne_Un, next.Item1));
			}
			for (int i = 0; i < brFalse.Count - 1; i++)
			{
				var brFrom = brFalse[i];
				var brTo = brFalse[i + 1];
				il.InsertAfter(brFrom.Item2, il.Create(OpCodes.Brfalse, brTo.Item1));
			}
			var lastBr = brFalse.Last();
			il.InsertAfter(lastBr.Item2, il.Create(OpCodes.Brfalse, endInstr));
			var lastBne = Bne.Last();
			il.InsertAfter(lastBne, il.Create(OpCodes.Bne_Un, endInstr));
		}
		void implementBoth(TypeDefinition t) {
			implement(t);
			implementByHeader(t);
		}
		foreach (var t in clientTypes) implementBoth(t);
		foreach (var t in serverTypes) implementBoth(t);
	}

	static void RecursivePublicize(TypeDefinition def) {
		def.IsPublic = true;
		if (def.DeclaringType != null)
			RecursivePublicize(def.DeclaringType);
	}
	static void RecursivePublicize(MethodDefinition def) {
		def.IsPublic = true;
		if (def.DeclaringType != null)
			RecursivePublicize(def.DeclaringType);
	}

	static void ImplementRouting(TypeDefinition[] allTypes, IEnumerable<TypeDefinition> clientTypes, IEnumerable<TypeDefinition> serverTypes, IEnumerable<(FieldDefinition, uint, TypeDefinition, TypeDefinition)> idMapping) {
		ConcurrentBag<TypeDefinition> typesToCheckFor = [];
		ConcurrentBag<TypeDefinition> blanketInterfaces = [];
		ConcurrentBag<TypeDefinition> handlerTypes = [];
		ConcurrentDictionary<TypeDefinition, TypeDefinition> implementingBlanket = [];
		bool interfaceCheck(TypeDefinition orig, TypeDefinition def) {
			foreach (var interf in def.Interfaces) {
				if (interf.InterfaceType.FullName == PacketInterfaceName) {
					typesToCheckFor.Add(orig);
					return true;
				}
			}
			return false;
		}
		TypeDefinition? blanketCheck(TypeDefinition def) {
			foreach (var interf in def.Interfaces) {
				foreach (var blanket in blanketInterfaces) {
					if (interf.InterfaceType.FullName == blanket.FullName) {
						return blanket;
					}
				}
			}
			return null;
		}
		Parallel.ForEach(allTypes, (type) =>
		{
			foreach (var attr in type.CustomAttributes) {
				if (attr.AttributeType.FullName == PacketHandlerName) {
					handlerTypes.Add(type);
					return;
				}
				else if (attr.AttributeType.FullName == BlanketInterfaceAttribute) {
					blanketInterfaces.Add(type);
					return;
				}
			}
			interfaceCheck(type, type);
		});
		Parallel.ForEach(allTypes, (type) =>
		{
			var blanket = blanketCheck(type);
			if (blanket != null) {
				implementingBlanket.TryAdd(type, blanket);
			}
		});
		List<(TypeDefinition, bool, bool, MethodDefinition, bool, TypeDefinition?)> routedTo = [];
		List<MethodDefinition> explicitRouting = [];
		List<MethodDefinition> specialize = [];
		List<(TypeReference, bool, bool, MethodDefinition)> routeInterface = [];
		HashSet<MethodDefinition> finalize = [];
		foreach (var handler in handlerTypes) {
			foreach (var method in handler.Methods) {
				try
				{
					bool routeLate = false;
					foreach (var attr in method.CustomAttributes)
					{
						if (attr.AttributeType.FullName == ExplicitHandler)
						{
							routeLate = true;
							explicitRouting.Add(method);
							break;
						}
						else if (attr.AttributeType.FullName == SpecializeBlanketAttribute) {
							specialize.Add(method);
							routeLate = true;
							break;
						}
					}
					if (routeLate) break;
					var firstParam = method.Parameters.FirstOrDefault();
					if (firstParam == null) continue;
					var elem = firstParam.ParameterType.GetElementType();
					if (elem is GenericParameter gen) {
						foreach (var constraint in gen.Constraints) {
							if (blanketInterfaces.Any((v) => v.FullName == constraint.FullName)) {
								var resT = constraint.Resolve();
								routeInterface.Add((resT, firstParam.ParameterType.IsByReference, resT.IsValueType, method));
								break;
							}
						}
						continue;
					}
					var resolvedType = elem.Resolve();
					if (!typesToCheckFor.Contains(resolvedType)) continue;
					routedTo.Add((resolvedType, firstParam.ParameterType.IsByReference, resolvedType.IsValueType, method, false, null));
				}
				catch (Exception ex) { Console.WriteLine(method.ToString());Console.WriteLine(ex.ToString()); }
			}
		}
		List<TypeDefinition> specialized = [];
		foreach (var method in specialize) {
			var firstParam = method.Parameters.FirstOrDefault();
			if (firstParam == null) continue;
			var resolvedType = firstParam.ParameterType.GetElementType().Resolve();
			specialized.Add(resolvedType);
			routedTo.Add((resolvedType, firstParam.ParameterType.IsByReference, resolvedType.IsValueType, method, false, null));
		}
		foreach (var intRoute in routeInterface) {
			var type = intRoute.Item1;
			var byRef = intRoute.Item2;
			var vt = intRoute.Item3;
			var method = intRoute.Item4;
			foreach (var kv in implementingBlanket) {
				if (specialized.Any(v => kv.Key.FullName == v.FullName)) continue;
				if (kv.Value.FullName != type.FullName) continue;
				routedTo.Add((kv.Key, byRef, vt, method, true, kv.Value));
			}
		}
		Dictionary<FieldDefinition, MethodDefinition> explicitRoutes = [];
		foreach (var m in explicitRouting) {
			RecursivePublicize(m);
			foreach (var attr in m.CustomAttributes)
			{
				if (attr.AttributeType.FullName == ExplicitHandler)
				{
					var firstArg = attr.ConstructorArguments.First();
					var caa = (CustomAttributeArgument)firstArg.Value;
					var argType = caa.Type.Resolve();
					foreach (var f in argType.Fields) {
						if (f == null || !f.HasConstant) continue;
						if ((ushort)f.Constant == (ushort)caa.Value) {
							explicitRoutes.Add(f, m);
						}
					}
					break;
				}
			}
		}
		if (explicitRoutes.Count > 0)
		{
			List<(uint, MethodDefinition)> mappedExplicit = [];
			foreach (var kv in explicitRoutes)
			{
				var mapped = idMapping.First((v) => v.Item1.FullName == kv.Key.FullName);
				mappedExplicit.Add((mapped.Item2, kv.Value));
			}
			uint lowestEmptyPacketId = idMapping.First((v) => v.Item3.FullName == EmptyBodyFullName).Item2;
			void implementServer(TypeDefinition type)
			{
				var m = type.Methods.First((v) => v.Name == "RouteExplicit");
				var mb = m.Body;
				mb.Instructions.Clear();
				var il = mb.GetILProcessor();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldc_I4, (int)lowestEmptyPacketId);
				Instruction after = il.Create(OpCodes.Sub);
				il.Append(after);
				List<Instruction> switchPoints = [];
				List<Instruction> jumpToEnd = [];
				foreach (var kv in explicitRoutes)
				{
					Instruction switchPoint = il.Create(OpCodes.Ldc_I4_1);
					il.Append(switchPoint);
					switchPoints.Add(switchPoint);
					il.Emit(OpCodes.Ldarg_2);
					Instruction jumpToEndInstr = il.Create(OpCodes.Call, kv.Value);
					il.Append(jumpToEndInstr);
					jumpToEnd.Add(jumpToEndInstr);
				}
				Instruction instr = il.Create(OpCodes.Switch, [.. switchPoints]);
				il.InsertAfter(after, instr);
				Instruction end = il.Create(OpCodes.Ret);
				jumpToEnd.Add(instr);
				il.Append(end);
				foreach (var jt in jumpToEnd)
				{
					il.InsertAfter(jt, il.Create(OpCodes.Br, end));
				}
			}
			void implementClient(TypeDefinition type)
			{
				var m = type.Methods.First((v) => v.Name == "RouteExplicit");
				var mb = m.Body;
				mb.Instructions.Clear();
				var il = mb.GetILProcessor();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldc_I4, (int)lowestEmptyPacketId);
				Instruction after = il.Create(OpCodes.Sub);
				il.Append(after);
				List<Instruction> switchPoints = [];
				List<Instruction> jumpToEnd = [];
				foreach (var kv in explicitRoutes)
				{
					Instruction switchPoint = il.Create(OpCodes.Ldc_I4_0);
					il.Append(switchPoint);
					switchPoints.Add(switchPoint);
					il.Emit(OpCodes.Ldc_I4_0);
					Instruction jumpToEndInstr = il.Create(OpCodes.Call, kv.Value);
					il.Append(jumpToEndInstr);
					jumpToEnd.Add(jumpToEndInstr);
				}
				Instruction instr = il.Create(OpCodes.Switch, [.. switchPoints]);
				il.InsertAfter(after, instr);
				Instruction end = il.Create(OpCodes.Ret);
				jumpToEnd.Add(instr);
				il.Append(end);
				foreach (var jt in jumpToEnd)
				{
					il.InsertAfter(jt, il.Create(OpCodes.Br, end));
				}
			}
			foreach (var client in clientTypes)
			{
				implementClient(client);
			}
			foreach (var server in serverTypes)
			{
				implementServer(server);
			}
		}
		foreach (var kv in routedTo) {
			RecursivePublicize(kv.Item1);
			RecursivePublicize(kv.Item4);
		}
		Dictionary<TypeDefinition, MethodDefinition> routes = [];
		foreach (var kv in routedTo) {
			var routed = kv.Item1;
			var to = kv.Item4;
			var isRef = kv.Item2;
			var typeIsVal = kv.Item3;
			var isIntf = kv.Item5;
			var intf = kv.Item6;
			MethodBody mb;
			if (routes.TryGetValue(routed, out var m)) {
				mb = m.Body;
			}
			else {
				m = new MethodDefinition("Route", MethodAttributes.Public, routed.Module.TypeSystem.Void);
				mb = new MethodBody(m);
				m.Body = mb;
				m.Parameters.Add(new ParameterDefinition(routed.Module.TypeSystem.Boolean));
				m.Parameters.Add(new ParameterDefinition(routed.Module.TypeSystem.UInt16));
				routed.Methods.Add(m);
				routed.Interfaces.Add(new InterfaceImplementation(routed.Module.ImportReference(typeof(IRoutedPacket))));
				finalize.Add(m);
				routes.Add(routed, m);
			}
			var il = mb.GetILProcessor();
			if (isIntf) {
				if (isRef && !routed.IsValueType) {
					il.Emit(OpCodes.Ldarga_S, (byte)0);
				}
				else if (!isRef && routed.IsValueType) {
					il.Emit(OpCodes.Ldarg_0);
					il.Emit(OpCodes.Ldobj, routed);
				}
				else {
					il.Emit(OpCodes.Ldarg_0);
				}
			}
			else if (isRef && !typeIsVal) {
				il.Emit(OpCodes.Ldarga_S, (byte)0);
			}
			else if (typeIsVal && !isRef) {
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldobj, routed);
			}
			else
			{
				il.Emit(OpCodes.Ldarg_0);
			}
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Ldarg_2);
			if (isIntf) {
				var genM = new GenericInstanceMethod(routed.Module.ImportReference(to));
				genM.GenericArguments.Add(routed);
				il.Emit(OpCodes.Call, genM);
			}
			else {
				il.Emit(OpCodes.Call, routed.Module.ImportReference(to));
			}
		}
		foreach (var m in finalize) {
			var body = m.Body;
			var il = body.GetILProcessor();
			il.Emit(OpCodes.Ret);
		}
	}

	static void ImplementRead(AssemblyDefinition thisAsm, IEnumerable<TypeDefinition> clients, IEnumerable<TypeDefinition> servers, IEnumerable<(FieldDefinition, uint, TypeDefinition, TypeDefinition)> idMapping) {
		foreach (var b in idMapping) {
			RecursivePublicize(b.Item3);
			RecursivePublicize(b.Item4);
		}
		TypeDefinition networkingSerde = new("tairasoul.unity.common.networking.serde", "RuntimeSerde", TypeAttributes.Public);
		MethodDefinition serialize = new("Serialize", MethodAttributes.Public | MethodAttributes.Static, thisAsm.MainModule.TypeSystem.Void);
		MethodDefinition deserializeAsync = new("DeserializeAsync", MethodAttributes.Public | MethodAttributes.Static, thisAsm.MainModule.ImportReference(typeof(Task<object?>)));
		var formatReader = thisAsm.MainModule.GetType("tairasoul.unity.common.format.FormatReader");
		var formatWriter = thisAsm.MainModule.GetType("tairasoul.unity.common.format.FormatWriter");
		var writeU32 = formatWriter.Methods.First((v) => v.Name == "Write" && v.Parameters.First().ParameterType == thisAsm.MainModule.TypeSystem.UInt32);
		{
			serialize.Parameters.Add(new ParameterDefinition(formatWriter));
			serialize.Parameters.Add(new ParameterDefinition(thisAsm.MainModule.TypeSystem.UInt32));
			serialize.Parameters.Add(new ParameterDefinition(thisAsm.MainModule.TypeSystem.Object));
			MethodBody serBody = new(serialize);
			serialize.Body = serBody;
			var il = serBody.GetILProcessor();
			List<Instruction> switchPoints = [];
			List<Instruction> jumpToEndPoints = [];
			foreach (var item in idMapping) {
				if (item.Item3.FullName == EmptyBodyFullName) {
					continue;
				}
				var instr = il.Create(OpCodes.Ldarg_0);
				il.Append(instr);
				switchPoints.Add(instr);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Call, writeU32);
				il.Emit(OpCodes.Ldarg_0);
				il.Emit(OpCodes.Ldarg_2);
				if (item.Item3.IsValueType)
				{
					il.Emit(OpCodes.Unbox_Any, thisAsm.MainModule.ImportReference(item.Item3));
				}
				else
				{
					il.Emit(OpCodes.Castclass, thisAsm.MainModule.ImportReference(item.Item3));
				}
				var instr2 = il.Create(OpCodes.Call, item.Item4.Methods.Single((v) => v.Name == "Serialize"));
				il.Append(instr2);
				jumpToEndPoints.Add(instr2);
			}
			var firstPoint = switchPoints.First();
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Ldarg_1));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Conv_I4));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Switch, [.. switchPoints]));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Ldstr, "got invalid packet id"));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Newobj, thisAsm.MainModule.ImportReference(typeof(Exception).GetConstructor([typeof(string)]))));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Throw));
			Instruction retBranch = il.Create(OpCodes.Ret);
			il.Append(retBranch);
			foreach (var toEnd in jumpToEndPoints) {
				il.InsertAfter(toEnd, Instruction.Create(OpCodes.Br, retBranch));
			}
			networkingSerde.Methods.Add(serialize);
		}
		{
			deserializeAsync.Parameters.Add(new ParameterDefinition(formatReader));
			deserializeAsync.Parameters.Add(new ParameterDefinition(thisAsm.MainModule.TypeSystem.UInt32));
			MethodBody desBody = new(deserializeAsync);
			deserializeAsync.Body = desBody;
			var il = desBody.GetILProcessor();
			List<Instruction> switchPoints = [];
			Instruction? emptyBodyInstr = null;
			var genConvert = thisAsm.MainModule.GetType("tairasoul.unity.common.networking.csmixer.NetworkingMixer").Methods.Single((v) => v.Name == "ConvertGeneric");
			foreach (var item in idMapping) {
				Instruction? instr1 = null;
				if (item.Item3.FullName != EmptyBodyFullName)
				{
					instr1 = il.Create(OpCodes.Ldarg_0);
					il.Append(instr1);
					il.Emit(OpCodes.Call, item.Item4.Methods.Single((v) => v.Name == "DeserializeAsync"));
				}
				else {
					if (emptyBodyInstr != null) {
						switchPoints.Add(emptyBodyInstr);
						continue;
					}
					instr1 = il.Create(OpCodes.Call, item.Item4.Methods.Single((v) => v.Name == "DeserializeAsync"));
					emptyBodyInstr = instr1;
					il.Append(instr1);
				}
				switchPoints.Add(instr1);
				var genRef = new GenericInstanceMethod(genConvert);
				genRef.GenericArguments.Add(item.Item3);
				il.Emit(OpCodes.Call, genRef);
				il.Emit(OpCodes.Ret);
			}
			var firstPoint = switchPoints.First();
			var retInstr = Instruction.Create(OpCodes.Ret);
			il.Append(retInstr);
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Ldarg_1));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Conv_I4));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Switch, [.. switchPoints]));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Ldstr, "got invalid packet id"));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Newobj, thisAsm.MainModule.ImportReference(typeof(Exception).GetConstructor([typeof(string)]))));
			il.InsertBefore(firstPoint, Instruction.Create(OpCodes.Throw));
			networkingSerde.Methods.Add(deserializeAsync);
		}
		thisAsm.MainModule.Types.Add(networkingSerde);
		var allPacketIds = idMapping.Select((v) => v.Item2).ToArray();
		foreach (var server in servers) {
			var desAsyncMethod = server.Methods.Single((v) => v.Name == "DeserializeAsync");
			var serMethod = server.Methods.Single((v) => v.Name == "WriteById");
			var idValidator = server.Methods.Single((v) => v.Name == "packetIdIsValid");
			var validIdField = new FieldDefinition("validPacketIds", Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Private, server.Module.ImportReference(typeof(uint[])));
			server.Fields.Add(validIdField);
			{
				var cctor = GetOrCreateStatic(server);
				var mb = cctor.Body;
				var mbI = mb.GetILProcessor();
				var lastInstr = mb.Instructions.Last();
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, allPacketIds.Length));
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Newarr, server.Module.TypeSystem.UInt32));
				for (int i = 0; i < allPacketIds.Length; i++)
				{
					mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Dup));
					mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, i));
					mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, (int)allPacketIds[i]));
					mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Stelem_I4));
				}
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Stsfld, validIdField));
			}
			{
				var il = serMethod.Body.GetILProcessor();
				serMethod.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Ldarg_3);
				il.Emit(OpCodes.Call, server.Module.ImportReference(serialize));
				il.Emit(OpCodes.Ret);
			}
			{
				var il = desAsyncMethod.Body.GetILProcessor();
				desAsyncMethod.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Call, server.Module.ImportReference(deserializeAsync));
				il.Emit(OpCodes.Ret);
			}
			{
				var il = idValidator.Body.GetILProcessor();
				idValidator.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldsfld, validIdField);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Call, server.Module.ImportReference(typeof(Enumerable).GetMethods().Single((v) => v.Name == "Contains" && v.GetParameters().Length == 2).MakeGenericMethod(typeof(uint))));
				il.Emit(OpCodes.Ret);
			}
		}
		foreach (var client in clients) {
			var desAsyncMethod = client.Methods.Single((v) => v.Name == "DeserializeAsync");
			var serMethod = client.Methods.Single((v) => v.Name == "WriteById");
			var idValidator = client.Methods.Single((v) => v.Name == "packetIdIsValid");
			var validIdField = new FieldDefinition("validPacketIds", Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Private, client.Module.ImportReference(typeof(uint[])));
			client.Fields.Add(validIdField);
			var cctor = GetOrCreateStatic(client);
			var mb = cctor.Body;
			var mbI = mb.GetILProcessor();
			var lastInstr = mb.Instructions.Last();
			mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, allPacketIds.Length));
			mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Newarr, client.Module.TypeSystem.UInt32));
			for (int i = 0; i < allPacketIds.Length; i++)
			{
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Dup));
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, i));
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Ldc_I4, (int)allPacketIds[i]));
				mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Stelem_I4));
			}
			mbI.InsertBefore(lastInstr, Instruction.Create(OpCodes.Stsfld, validIdField));
			{
				var il = desAsyncMethod.Body.GetILProcessor();
				desAsyncMethod.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Call, client.Module.ImportReference(deserializeAsync));
				il.Emit(OpCodes.Ret);
			}
			{
				var il = serMethod.Body.GetILProcessor();
				serMethod.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Ldarg_2);
				il.Emit(OpCodes.Ldarg_3);
				il.Emit(OpCodes.Call, client.Module.ImportReference(serialize));
				il.Emit(OpCodes.Ret);
			}
			{
				var il = idValidator.Body.GetILProcessor();
				idValidator.Body.Instructions.Clear();
				il.Emit(OpCodes.Ldsfld, validIdField);
				il.Emit(OpCodes.Ldarg_1);
				il.Emit(OpCodes.Call, client.Module.ImportReference(typeof(Enumerable).GetMethods().Single((v) => v.Name == "Contains" && v.GetParameters().Length == 2).MakeGenericMethod(typeof(uint))));
				il.Emit(OpCodes.Ret);
			}
		}
	}

	static TypeDefinition ImplementEmpty(AssemblyDefinition thisAsm, TypeDefinition[] allTypes) {
		ConcurrentBag<FieldDefinition> allEmpty = [];
		Parallel.ForEach(allTypes, (type) =>
		{
			if (type.IsEnum && type.CustomAttributes.Any((v) => v.AttributeType.FullName == PacketIdentifier)) {
				foreach (var field in type.Fields) {
					if (!field.IsPublic) continue;
					if (!field.CustomAttributes.Any((v) => v.AttributeType.FullName == CorrelatesTo || v.AttributeType.FullName == CorrelatesToInternal)) {
						allEmpty.Add(field);
					}
				}
			}
		});
		foreach (var fd in allEmpty) {
			var constrRef = fd.Module.ImportReference(typeof(CorrelatesTo).GetConstructor([typeof(Type)]));
			var typeRef = fd.Module.ImportReference(typeof(Type));
			var attr = new CustomAttribute(constrRef);
			var arg = new CustomAttributeArgument(typeRef, fd.Module.ImportReference(typeof(EmptyBody)));
			attr.ConstructorArguments.Add(arg);
			fd.CustomAttributes.Add(attr);
		}
		TypeDefinition emptyFormatter = new("formatters_tairasoul.unity.common.networking.empty", "EmptyBodyFormatter", TypeAttributes.Public | TypeAttributes.Class);
		thisAsm.MainModule.Types.Add(emptyFormatter);
		{
			var constrRef = thisAsm.MainModule.ImportReference(typeof(FormatMethodsForAttribute).GetConstructor([typeof(Type)]));
			var typeRef = thisAsm.MainModule.ImportReference(typeof(Type));
			var attr = new CustomAttribute(constrRef);
			var arg = new CustomAttributeArgument(typeRef, thisAsm.MainModule.ImportReference(typeof(EmptyBody)));
			attr.ConstructorArguments.Add(arg);
			emptyFormatter.CustomAttributes.Add(attr);
		}
		FieldDefinition instanceField;
		{
			instanceField = new("Instance", FieldAttributes.Static | FieldAttributes.Public, thisAsm.MainModule.ImportReference(typeof(EmptyBody)));
			emptyFormatter.Fields.Add(instanceField);
			var staticCtor = GetOrCreateStatic(emptyFormatter);
			var mb = staticCtor.Body;
			var il = mb.GetILProcessor();
			mb.Instructions.Clear();
			il.Emit(OpCodes.Newobj, thisAsm.MainModule.ImportReference(typeof(EmptyBody).GetConstructor([])));
			il.Emit(OpCodes.Stsfld, instanceField);
			il.Emit(OpCodes.Ret);
		}
		{
			MethodDefinition desAsync = new("DeserializeAsync", MethodAttributes.Public | MethodAttributes.Static, thisAsm.MainModule.ImportReference(typeof(Task<EmptyBody>)));
			var mb = new MethodBody(desAsync);
			desAsync.Body = mb;
			var il = mb.GetILProcessor();
			il.Emit(OpCodes.Ldsfld, instanceField);
			var fromResult = thisAsm.MainModule.ImportReference(typeof(Task).GetMethod("FromResult").MakeGenericMethod(typeof(EmptyBody)));
			il.Emit(OpCodes.Call, fromResult);
			il.Emit(OpCodes.Ret);
			emptyFormatter.Methods.Add(desAsync);
		}
		{
			MethodDefinition serialize = new("Serialize", MethodAttributes.Public | MethodAttributes.Static, thisAsm.MainModule.TypeSystem.Void);
			var mb = new MethodBody(serialize);
			serialize.Body = mb;
			var il = mb.GetILProcessor();
			il.Emit(OpCodes.Ret);
			emptyFormatter.Methods.Add(serialize);
		}
		return emptyFormatter;
	}

	static MethodDefinition GetOrCreateStatic(TypeDefinition typeDef) {
		foreach (var m in typeDef.Methods) {
			if (m.IsConstructor && m.Name == ".cctor") return m;
		}
		var md = new MethodDefinition(".cctor", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName, typeDef.Module.TypeSystem.Void);
		var mb = new MethodBody(md);
		md.Body = mb;
		mb.GetILProcessor().Emit(OpCodes.Ret);
		typeDef.Methods.Add(md);
		return md;
	}

	static IEnumerable<(FieldDefinition, uint, TypeDefinition, TypeDefinition)> GetIdMapping(TypeDefinition[] allTypes) {
		ConcurrentBag<(FieldDefinition, TypeDefinition, TypeDefinition)> formatters = [];
		ConcurrentBag<(FieldDefinition, TypeDefinition, TypeDefinition)> emptyFormatters = [];
		ConcurrentBag<(FieldDefinition, TypeDefinition)> checkForFormat = [];
		Parallel.ForEach(allTypes, (type) =>
		{
			if (type.CustomAttributes.Any((v) => v.AttributeType.FullName == PacketIdentifier))
			{
				foreach (var field in type.Fields)
				{
					if (!field.IsPublic) continue;
					if (!field.HasConstant) continue;
					foreach (var attr in field.CustomAttributes)
					{
						if (attr.AttributeType.FullName == CorrelatesTo)
						{
							var corrRef = (TypeReference)attr.ConstructorArguments.First().Value;
							var resolvedRef = corrRef.Resolve();
							checkForFormat.Add((field, resolvedRef));
						}
					}
				}
			}
		});
		Parallel.ForEach(allTypes, (type) =>
		{
			if (!type.Namespace.StartsWith("formatters_")) return;
			foreach (var attr in type.CustomAttributes)
			{
				if (attr.AttributeType.FullName == FormatMethodsFor)
				{
					var target = (TypeReference)attr.ConstructorArguments.First().Value;
					foreach (var c in checkForFormat)
					{
						var check = c.Item2;
						if (check.FullName == target.FullName)
						{
							if (check.FullName == EmptyBodyFullName)
								emptyFormatters.Add((c.Item1, check, type));
							else
								formatters.Add((c.Item1, check, type));
						}
					}
				}
			}
		});
		uint packetCount = 0;
		List<(FieldDefinition, uint, TypeDefinition, TypeDefinition)> mapping = [];
		HashSet<string> alreadyAdded = [];
		{
			ConcurrentBag<(ulong high, ulong low, FieldDefinition, TypeDefinition, TypeDefinition)> murmurHashed = [];
			Parallel.ForEach(formatters, (pair) =>
			{
				var origin = pair.Item2;
				var target = pair.Item3;
				var originAsm = origin.Module.Assembly;
				if (originAsm.FullName == Assembly.GetExecutingAssembly().FullName)
				{
					murmurHashed.Add((0, 0, pair.Item1, origin, target));
				}
				else
				{
					(ulong high, ulong low) = Murmur3.Hash128(originAsm.FullName);
					murmurHashed.Add((high, low, pair.Item1, origin, target));
				}
			});
			IEnumerable<(ulong high, ulong low, FieldDefinition field, TypeDefinition origin, TypeDefinition formatter)> ordered = murmurHashed
				.OrderBy((item) => item.high)
				.ThenBy((item) => item.low);
			foreach (var (high, low, field, origin, formatter) in ordered)
			{
				if (alreadyAdded.Add(field.FullName))
					mapping.Add((field, packetCount++, origin, formatter));
			}
		}
		{
			ConcurrentBag<(ulong high, ulong low, FieldDefinition, TypeDefinition, TypeDefinition)> murmurHashed = [];
			Parallel.ForEach(emptyFormatters, (pair) =>
			{
				var origin = pair.Item1;
				var target = pair.Item3;
				var originAsm = origin.Module.Assembly;
				if (originAsm.FullName == Assembly.GetExecutingAssembly().FullName)
				{
					murmurHashed.Add((0, 0, origin, pair.Item2, target));
				}
				else
				{
					(ulong high, ulong low) = Murmur3.Hash128(originAsm.FullName);
					murmurHashed.Add((high, low, origin, pair.Item2, target));
				}
			});
			IEnumerable<(ulong high, ulong low, FieldDefinition field, TypeDefinition origin, TypeDefinition formatter)> ordered = murmurHashed
				.OrderBy((item) => item.high)
				.ThenBy((item) => item.low);
			foreach (var (high, low, field, origin, formatter) in ordered)
			{
				if (alreadyAdded.Add(field.FullName))
					mapping.Add((field, packetCount++, origin, formatter));
			}
		}
		return mapping;
	}
}