using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using tairasoul.unity.common.hashing;
using tairasoul.unity.common.networking.attributes.packets;
using PacketReliability = tairasoul.unity.common.networking.layer.PacketReliability;

namespace tairasoul.unity.common.networking.registries;

public record NetworkPacketInfo(object @enum, int id, Type? assocType, PacketReliability? reliability, ServerRelayType? relay);

public static class NetworkPacketRegistry {
	internal static int bitCount = 0;
	internal static ConcurrentDictionary<object, NetworkPacketInfo> info = [];
	internal static ConcurrentDictionary<Assembly, List<NetworkPacketInfo>> toRegister = [];
	internal static bool registered = false;

	static NetworkPacketRegistry() {
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies) {
			Type[] types = assembly.GetTypes();
			foreach (Type type in types) {
				if (Attribute.IsDefined(type, typeof(PacketTypeIdentifier))) {
					var fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static);
					foreach (var field in fieldInfos) {
						PacketReliability? rel = null;
						ServerRelayType? relay = null;
						Type? assocType = null;
						var ident = field.GetCustomAttributes<Reliability>();
						ServerRelay? relay1 = field.GetCustomAttribute<ServerRelay>();
						CorrelatesTo? correlates = field.GetCustomAttribute<CorrelatesTo>();
						CorrelatesToInternal? icorrelates = field.GetCustomAttribute<CorrelatesToInternal>();
						bool foundReliable = false;
						bool foundUnreliable = false;
						foreach (var id in ident) {
							if (id.reliability == attributes.packets.PacketReliability.Reliable) {
								foundReliable = true;
							}
							else if (id.reliability == attributes.packets.PacketReliability.Unreliable) {
								foundUnreliable = true;
							}
						}
						if (foundReliable && foundUnreliable) {}
						else if (foundReliable) {
							rel = PacketReliability.Reliable;
						}
						else if (foundUnreliable) {
							rel = PacketReliability.Unreliable;
						}
						else {
							rel = PacketReliability.Reliable;
						}
						if (relay1 != null) {
							relay = relay1.handling;
						}
						if (correlates != null) {
							assocType = correlates.correlation;
						}
						if (icorrelates != null) {
							switch (icorrelates.packet) {
								case InternalPacketTypes.Connect:
								case InternalPacketTypes.PlayerConnected:
								case InternalPacketTypes.IdRelay:
									foreach (var ty in types) {
										if (ty.FullName == "tairasoul.unity.common.networking.gentypes.InternalIdRelayPacket" 
										|| ty.FullName == "tairasoul.unity.common.networking.gentypes.InternalConnectPacket"
										|| ty.FullName == "tairasoul.unity.common.networking.gentypes.InternalPlayerConnectedPacket") {
											assocType = ty;
										}
									}
									break;
							}
						}
						Register(assembly, field.GetValue(null), assocType, rel, relay);
					}
				}
			}
		}
		CompleteRegistration();
	}

	public static NetworkPacketInfo GetPacketInfo<T>() {
		return GetPacketInfo(typeof(T));
	}

	public static NetworkPacketInfo GetPacketInfo(Type dataType) {
		foreach (var value in info.Values) {
			if (value.assocType == dataType)
				return value;
		}
		throw new KeyNotFoundException($"Could not find info for packet data {dataType}");
	}

	public static NetworkPacketInfo GetPacketInfo(object @enum) {
		if (!info.TryGetValue(@enum, out var _info))
			throw new KeyNotFoundException($"Could not find info for packet {@enum}");
		return _info;
	}

	public static NetworkPacketInfo GetPacketInfo(int id) {
		foreach (var pair in info) {
			if (pair.Value.id == id) {
				return pair.Value;
			}
		}
		throw new KeyNotFoundException($"Could not find info for id {id}");
	}

	static void Register(Assembly asm, object @enum, Type? assocType, PacketReliability? reliability, ServerRelayType? relay) {
		if (registered) return;
		if (!toRegister.TryGetValue(asm, out List<NetworkPacketInfo> packets))
			packets = [];
		packets.Add(new NetworkPacketInfo(@enum, (int)@enum, assocType, reliability, relay));
		toRegister.TryAdd(asm, packets);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int BitLength(int n) => n == 0 ? 1 : 32 - LeadingZeroCount(n);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int LeadingZeroCount(int n)
	{
		if (n == 0) return 32;
		int count = 0;
		while ((n & 0x80000000) == 0)
		{
			count++;
			n <<= 1;
		}
		return count;
	}

	internal static void CompleteRegistration() {
		if (registered) return;
		List<(ulong high, ulong low, List<NetworkPacketInfo> info)> pairs = [];
		foreach (var item in toRegister) {
			if (item.Key == Assembly.GetExecutingAssembly()) {
				pairs.Add((0, 0, item.Value));
			}
			else {
				(ulong high, ulong low) = Murmur3.Hash128(item.Key.FullName);
				pairs.Add((high, low, item.Value));
			}
		}
		List<(ulong high, ulong low, List<NetworkPacketInfo> info)> ordered = [.. pairs
			.OrderBy((item) => item.high)
			.ThenBy((item) => item.low)];
		int packetCount = 0;
		foreach (var item in ordered) {
			foreach (var packet in item.info) {
				NetworkPacketInfo newInfo = packet with { id = packetCount++ };
				info.TryAdd(packet.@enum, newInfo);
			}
		}
		bitCount = BitLength(packetCount);
		toRegister.Clear();
		registered = true;
	}
}