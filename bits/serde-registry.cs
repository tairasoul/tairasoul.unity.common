using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace tairasoul.unity.common.bits;

record SerdeInfo(
	Action<BitWriter, object> syncWrite,
	Func<BitReader, object> syncRead,
	Func<BitWriterAsync, object, Task> asyncWrite,
	Func<BitReaderAsync, Task<object>> asyncRead
);

public static class SerdeRegistry
{
	static ConcurrentDictionary<Type, SerdeInfo> SerdeSets = [];

	static SerdeRegistry() {
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly asm in assemblies) {
			var types = asm.GetTypes();
			foreach (Type type in types) {
				if (type.Namespace.EndsWith("serde")) {
					var nsp = type.Namespace.Split('.');
					var parentNs = string.Join(".", nsp.Take(nsp.Length - 1));
					var assocTypeName = type.Name.Substring(0, type.Name.Length - 5);
					var assocType = types.First((v) => v.Namespace == parentNs && v.Name == assocTypeName);
					SerdeSets.TryAdd(assocType, BuildSerdeInfo(assocType, type));
				}
			}
		}
	}

	static SerdeInfo BuildSerdeInfo(Type parentType, Type serdeType) {
		ParameterExpression asyncWriterParam = Expression.Parameter(typeof(BitWriterAsync));
		ParameterExpression asyncReaderParam = Expression.Parameter(typeof(BitReaderAsync));
		ParameterExpression syncWriterParam = Expression.Parameter(typeof(BitWriter));
		ParameterExpression syncReaderParam = Expression.Parameter(typeof(BitReader));
		ParameterExpression objectParam = Expression.Parameter(typeof(object));
		Expression convertedParam = Expression.Convert(objectParam, parentType);
		Expression serAsync = Expression.Call(serdeType.GetMethod("SerializeAsync"), asyncWriterParam, convertedParam);
		Expression desAsync = Expression.Call(serdeType.GetMethod("DeserializeAsync"), asyncReaderParam);
		Expression serSync = Expression.Call(serdeType.GetMethod("Serialize"), syncWriterParam, convertedParam);
		Expression desSync = Expression.Call(serdeType.GetMethod("Deserialize"), syncReaderParam);
		var serAsyncAction = Expression.Lambda<Func<BitWriterAsync, object, Task>>(serAsync, asyncWriterParam, objectParam).Compile();
		var desAsyncAction = Expression.Lambda<Func<BitReaderAsync, Task<object>>>(desAsync, asyncReaderParam).Compile();
		var serSyncAction = Expression.Lambda<Action<BitWriter, object>>(serSync, syncWriterParam, objectParam).Compile();
		var desSyncAction = Expression.Lambda<Func<BitReader, object>>(desSync, syncReaderParam).Compile();
		return new(serSyncAction, desSyncAction, serAsyncAction, desAsyncAction);
	}

	public static void Serialize<T>(BitWriter writer, T data) {
		if (data == null) return;
		Serialize(writer, data, typeof(T));
	}

	public static void Serialize(BitWriter writer, object data, Type dataType) {
		if (data == null) return;
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		serde.syncWrite(writer, data);
	}

	public static async Task Serialize<T>(BitWriterAsync writer, T data) {
		if (data == null) return;
		await Serialize(writer, data, typeof(T));
	}

	public static async Task Serialize(BitWriterAsync writer, object data, Type dataType) {
		if (data == null) return;
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		await serde.asyncWrite(writer, data);
	}

	public static T Deserialize<T>(BitReader reader) {
		return (T)Deserialize(reader, typeof(T));
	}

	public static object Deserialize(BitReader reader, Type dataType) {
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		return serde.syncRead(reader);
	}

	public static async Task<T> Deserialize<T>(BitReaderAsync reader) {
		return (T)await Deserialize(reader, typeof(T));
	}

	public static async Task<object> Deserialize(BitReaderAsync reader, Type dataType) {
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		return await serde.asyncRead(reader);
	}
}