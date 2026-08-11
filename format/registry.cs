using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using tairasoul.unity.common.format.attributes;

namespace tairasoul.unity.common.format;

record RegisteredFormatItem(
	Action<object, FormatWriter> ser,
	Func<FormatReader, object> des,
	Func<FormatReader, Task<object>> desAsync
);

public static class FormatRegistry
{
	static ConcurrentDictionary<RuntimeTypeHandle, RegisteredFormatItem> reg = [];

	static FormatRegistry()
	{
		HashSet<string> encounteredTypes = [];
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (var assembly in assemblies) {
			Type[] types = assembly.GetTypes();
			foreach (var type in types) {
				if (type.Namespace != null && type.Namespace.StartsWith("tairasoul.unity.common.format.items.formatters_")) {
					var @for = type.GetCustomAttribute<FormatMethodsForAttribute>();
					if (@for == null) continue;
					if (!encounteredTypes.Add(@for.target.FullName)) continue;
					var res = BuildInfo(@for.target, type);
					reg[@for.target.TypeHandle] = res;
				}
			}
		}
	}

	static async Task<object> ConvertGeneric<T>(Task<T> task)
	{
		T res = await task;
		return res!;
	}

	static RegisteredFormatItem BuildInfo(Type parent, Type target)
	{
		DynamicMethod ser = new(
			"Serialize",
			typeof(void),
			[typeof(object), typeof(FormatWriter)]
		);
		var sg = ser.GetILGenerator();
		sg.Emit(OpCodes.Ldarg_1);
		sg.Emit(OpCodes.Ldarg_0);
		if (parent.IsValueType)
		{
			sg.Emit(OpCodes.Unbox_Any, parent);
		}
		sg.Emit(OpCodes.Call, target.GetMethod("Serialize", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));
		sg.Emit(OpCodes.Ret);
		DynamicMethod des = new(
			"Deserialize",
			typeof(object),
			[typeof(FormatReader)]
		);
		var dg = des.GetILGenerator();
		dg.Emit(OpCodes.Ldarg_0);
		dg.Emit(OpCodes.Call, target.GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));
		if (parent.IsValueType)
		{
			dg.Emit(OpCodes.Box, parent);
		}
		dg.Emit(OpCodes.Ret);
		DynamicMethod desA = new(
			"DeserializeAsync",
			typeof(Task<object>),
			[typeof(FormatReader)]
		);
		var dag = desA.GetILGenerator();
		dag.Emit(OpCodes.Ldarg_0);
		dag.Emit(OpCodes.Call, target.GetMethod("DeserializeAsync", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));
		dag.Emit(OpCodes.Call, typeof(FormatRegistry).GetMethod(nameof(ConvertGeneric), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(parent));
		dag.Emit(OpCodes.Ret);
		Action<object, FormatWriter> serD = (Action<object, FormatWriter>)ser.CreateDelegate(typeof(Action<object, FormatWriter>));
		Func<FormatReader, object> desD = (Func<FormatReader, object>)des.CreateDelegate(typeof(Func<FormatReader, object>));
		Func<FormatReader, Task<object>> desAD = (Func<FormatReader, Task<object>>)desA.CreateDelegate(typeof(Func<FormatReader, Task<object>>));
		return new(serD, desD, desAD);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Serialize<T>(T data, FormatWriter writer)
	{
		Serialize(data, typeof(T), writer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Deserialize<T>(FormatReader reader) {
		return (T)Deserialize(typeof(T), reader);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static async Task<T> DeserializeAsync<T>(FormatReader reader) {
		return (T)await DeserializeAsync(typeof(T), reader);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Serialize(object data, Type type, FormatWriter writer)
	{
		reg.TryGetValue(type.TypeHandle, out var info);
		info!.ser(data, writer);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static object Deserialize(Type type, FormatReader reader) {
		reg.TryGetValue(type.TypeHandle, out var info);
		return info!.des(reader);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static async Task<object> DeserializeAsync(Type type, FormatReader reader) {
		reg!.TryGetValue(type.TypeHandle, out var info);
		return await info!.desAsync(reader);
	}
}