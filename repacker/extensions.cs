using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Collections.Generic;

namespace tairasoul.unity.common.repacker.extensions;

static class CollectionsExtension {
	static FieldInfo size = typeof(Collection<>).GetField("size", BindingFlags.Instance | BindingFlags.NonPublic);

	static Dictionary<RuntimeTypeHandle, FieldInfo> cache = [];

	static FieldInfo GetSize<T>() {
		if (cache.TryGetValue(typeof(T).TypeHandle, out var f)) return f;
		var fieldInfo = typeof(Collection<T>).GetField("size", BindingFlags.Instance | BindingFlags.NonPublic);
		cache[typeof(T).TypeHandle] = fieldInfo;
		return fieldInfo;
	}

	public static void ReplaceSlot<T>(this Collection<T> collection, int idx, T element) {
		if (collection.Capacity < idx + 1) collection.Capacity = idx + 1;
		GetSize<T>().SetValue(collection, collection.Capacity);
		collection[idx] = element;
	}
}

static class TypeExtensions {
	public static ConstructorInfo GetConstructor(this Type type, BindingFlags flags, Type[] types) {
		return type.GetConstructor(flags, null, types, null);
	}
}