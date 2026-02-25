using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using tairasoul.unity.common.attributes.bits;

namespace tairasoul.unity.common.bits;

record SerdeInfo(
	Action<object, BitWriter> syncWrite,
	Delegate syncRead,
	Func<object, BitWriterAsync, Task> asyncWrite,
	Delegate asyncRead
);

public static class SerdeRegistry
{
	static ConcurrentDictionary<Type, SerdeInfo> SerdeSets = [];

	static SerdeRegistry() {
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly asm in assemblies) {
			var types = asm.GetTypes();
			foreach (Type type in types) {
				if (type.Namespace != null && type.Namespace.EndsWith("serde")) {
					SerdeForType? serde = type.GetCustomAttribute<SerdeForType>();
					if (serde == null) continue;
					SerdeSets.TryAdd(serde.type, BuildSerdeInfo(serde.type, type));
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
		Expression serAsync = Expression.Call(serdeType.GetMethod("SerializeAsync"), convertedParam, asyncWriterParam);
		Expression desAsync = Expression.Call(serdeType.GetMethod("DeserializeAsync"), asyncReaderParam);
		Expression serSync = Expression.Call(serdeType.GetMethod("Serialize"), convertedParam, syncWriterParam);
		Expression desSync = Expression.Call(serdeType.GetMethod("Deserialize"), syncReaderParam);
		Type TaskType = typeof(Task<>);
		Type desAsyncType = typeof(Func<,>).MakeGenericType(typeof(BitReaderAsync), TaskType.MakeGenericType(parentType));
		Type desSyncType = typeof(Func<,>).MakeGenericType(typeof(BitReader), parentType);
		var serAsyncAction = Expression.Lambda<Func<object, BitWriterAsync, Task>>(serAsync, objectParam, asyncWriterParam).Compile();
		var desAsyncAction = Expression.Lambda(desAsyncType, desAsync, asyncReaderParam).Compile();
		var serSyncAction = Expression.Lambda<Action<object, BitWriter>>(serSync, objectParam, syncWriterParam).Compile();
		var desSyncAction = Expression.Lambda(desSyncType, desSync, syncReaderParam).Compile();
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
		serde.syncWrite(data, writer);
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
		await serde.asyncWrite(data, writer);
	}

	public static T Deserialize<T>(BitReader reader) {
		return (T)Deserialize(reader, typeof(T));
	}

	public static object Deserialize(BitReader reader, Type dataType) {
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		return serde.syncRead.DynamicInvoke(reader)!;
	}

	public static async Task<T> Deserialize<T>(BitReaderAsync reader) {
		return (T)await Deserialize(reader, typeof(T));
	}

	public static async Task<object> Deserialize(BitReaderAsync reader, Type dataType) {
		if (!SerdeSets.TryGetValue(dataType, out SerdeInfo serde)) {
			throw new KeyNotFoundException($"Type {dataType} is not a ser/de type.");
		}
		return await (Task<object>)serde.asyncRead.DynamicInvoke(reader);
	}
}