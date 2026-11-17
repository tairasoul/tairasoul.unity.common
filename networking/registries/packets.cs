using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using tairasoul.unity.common.hashing;
using tairasoul.unity.common.networking.attributes.packets;
using vapmp;

namespace tairasoul.unity.common.networking.registries;

public record NetworkPacketInfo(object @enum, uint id, Type? assocType);

public static class NetworkPacketRegistry {
	internal static ConcurrentDictionary<object, NetworkPacketInfo> info = [];
	internal static ConcurrentDictionary<Assembly, List<NetworkPacketInfo>> toRegister = [];
	internal static bool registered = false;

	static NetworkPacketRegistry() {
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		Array.Sort(assemblies, (x, y) => string.Compare(x.FullName, y.FullName));
		foreach (Assembly assembly in assemblies) {
			Type[] types = assembly.GetTypes();
			foreach (Type type in types) {
				if (Attribute.IsDefined(type, typeof(PacketTypeIdentifier))) {
					var fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static);
					foreach (var field in fieldInfos) {
						Type? assocType = null;
						CorrelatesTo? correlates = field.GetCustomAttribute<CorrelatesTo>();
						if (correlates != null) {
							assocType = correlates.correlation;
						}
						Register(assembly, field.GetValue(null), assocType);
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
			Plugin.Log.LogInfo($"{dataType} {value}");
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

	public static NetworkPacketInfo GetPacketInfo(uint id) {
		foreach (var pair in info) {
			if (pair.Value.id == id) {
				return pair.Value;
			}
		}
		throw new KeyNotFoundException($"Could not find info for id {id}");
	}

	static void Register(Assembly asm, object @enum, Type? assocType) {
		if (registered) return;
		if (!toRegister.TryGetValue(asm, out List<NetworkPacketInfo> packets))
			packets = [];
		packets.Add(new NetworkPacketInfo(@enum, (uint)@enum, assocType));
		toRegister.TryAdd(asm, packets);
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
		IEnumerable<(ulong high, ulong low, List<NetworkPacketInfo> info)> ordered = pairs
			.OrderBy((item) => item.high)
			.ThenBy((item) => item.low);
		List<NetworkPacketInfo> withoutData = [];
		uint packetCount = 0;
		foreach (var item in ordered) {
			foreach (var packet in item.info) {
				if (packet.assocType == null) {
					withoutData.Add(packet);
					continue;
				}
				NetworkPacketInfo newInfo = packet with { id = packetCount++ };
				info.TryAdd(newInfo.@enum, newInfo);
			}
		}
		foreach (var without in withoutData) {
			NetworkPacketInfo newInfo = without with { id = packetCount++ };
			info.TryAdd(newInfo.@enum, newInfo);
		}
		toRegister.Clear();
		registered = true;
	}
}