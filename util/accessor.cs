using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
#if ACCESSOR_INCLUDE_FINDFUNC
using UnityEngine;
#endif

namespace tairasoul.unity.common.util;

record AccessorGetCacheKey(Type type, string fieldName);
// record AccessorSetCacheKey(Type type, string fieldName);
// record AccessorCallCacheKey(Type type, string methodName, Type[] argTypes);

public static class AccessorUtil {
	internal static Dictionary<AccessorGetCacheKey, Func<object, object>> CachedGetAccessors = [];
	// internal static Dictionary<AccessorSetCacheKey, Func<object, object>> CachedSetAccessors = [];
#if ACCESSOR_INCLUDE_FINDFUNC
	internal static Dictionary<string, GameObject> GameObjectCache = [];
#endif

	internal static void RemoveElement(Type type, string fieldName) {
		CachedGetAccessors.Remove(new(type, fieldName));
		// CachedSetAccessors.Remove(new(type, fieldName));
	}

	internal static void ClearCache()
	{
		CachedGetAccessors.Clear();
		// CachedSetAccessors.Clear();
#if ACCESSOR_INCLUDE_FINDFUNC
		GameObjectCache.Clear();
#endif
	}
	
#if ACCESSOR_INCLUDE_FINDFUNC
	static IEnumerable<string> SplitUnescaped(string text, char separator, char escape = '\\')
	{
		var buffer = new StringBuilder();
		bool escaped = false;

		List<string> res = [];

		foreach (char c in text)
		{
			if (c == escape && !escaped)
			{
				escaped = true;
			}
			else if (c == separator && !escaped)
			{
				res.Add(buffer.ToString());
				buffer.Clear();
			}
			else
			{
				buffer.Append(c);
				escaped = false;
			}
		}
		res.Add(buffer.ToString());
		return res;
	}

	public static GameObject? FindGameObject(string path) {
		if (GameObjectCache.TryGetValue(path, out var go) && go != null)
			return go;
		IEnumerable<string> split = SplitUnescaped(path, '/');
		GameObject current = GameObject.Find(split.ElementAt(0));
		if (current == null) return null;
		for (int i = 1; i < split.Count(); i++)
		{
			GameObject found = current.transform.Find(split.ElementAt(i)).gameObject;
			if (found == null) return null;
			current = found;
		}
		GameObjectCache[path] = current;
		return current;
	}
#endif

	/*public static void Set<T>(object instance, Type type, string fieldName, T value) {
		AccessorSetCacheKey key = new(type, fieldName);
		if (CachedSetAccessors.TryGetValue(key, out var accessor)) {
			accessor(value);
			return;
		}
	}*/

	public static T Get<T, C>(C instance, string fieldName) {
		return Get<T>(instance, typeof(C), fieldName);
	}

	public static T Get<T>(object instance, Type type, string fieldName) {
		AccessorGetCacheKey key = new(type, fieldName);
		if (CachedGetAccessors.TryGetValue(key, out var accessor)) {
			return (T)accessor(instance);
		}
		FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
		if (field != null) {
			if (field.IsStatic) {
				if (field.IsPublic) {
					Expression expression = Expression.Field(null, field);
					Func<object> access = Expression.Lambda<Func<object>>(expression).Compile();
					CachedGetAccessors[key] = (_) => access();
					return (T)access();
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(object),
						[typeof(object)],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldsfld, field);
					gen.Emit(OpCodes.Box, field.FieldType);
					gen.Emit(OpCodes.Ret);
					Func<object> access = (Func<object>)dm.CreateDelegate(typeof(Func<object>));
					CachedGetAccessors[key] = (_) => access();
					return (T)access();
				}
			}
			else {
				if (field.IsPublic) {
					ParameterExpression param = Expression.Parameter(typeof(object), "__instance");
					Expression instanceCast = Expression.Convert(param, field.DeclaringType);
					Expression expression = Expression.Field(instanceCast, field);
					Expression result = Expression.Convert(expression, typeof(object));
					Func<object, object> access = Expression.Lambda<Func<object, object>>(result, param).Compile();
					CachedGetAccessors[key] = access;
					return (T)access(instance);
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(object),
						[typeof(object)],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldarg_0);
					gen.Emit(OpCodes.Castclass, type);
					gen.Emit(OpCodes.Ldfld, field);
					gen.Emit(OpCodes.Box, field.FieldType);
					gen.Emit(OpCodes.Ret);
					Func<object, object> access = (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
					CachedGetAccessors[key] = access;
					return (T)access(instance);
				}
			}
		}
		PropertyInfo property = type.GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
		if (property != null) {
			if (property.GetMethod.IsStatic) {
				if (property.GetMethod.IsPublic) {
					Expression expression = Expression.Call(null, property.GetGetMethod());
					Expression result = Expression.Convert(expression, typeof(object));
					Func<object> access = Expression.Lambda<Func<object>>(result).Compile();
					CachedGetAccessors[key] = (_) => access();
					return (T)access();
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(object),
						[typeof(object)],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Call, property.GetGetMethod(true));
					gen.Emit(OpCodes.Box, property.PropertyType);
					gen.Emit(OpCodes.Ret);
					Func<object> access = (Func<object>)dm.CreateDelegate(typeof(Func<object>));
					CachedGetAccessors[key] = (_) => access();
					return (T)access();
				}
			}
			else {
				if (property.GetMethod.IsPublic) {
					ParameterExpression param = Expression.Parameter(typeof(object), "__instance");
					Expression instanceCast = Expression.Convert(param, property.DeclaringType);
					Expression expression = Expression.Call(instanceCast, property.GetGetMethod());
					Expression result = Expression.Convert(expression, typeof(object));
					Func<object, object> access = Expression.Lambda<Func<object, object>>(result, param).Compile();
					CachedGetAccessors[key] = access;
					return (T)access(instance);
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(object),
						[typeof(object)],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldarg_0);
					gen.Emit(OpCodes.Castclass, type);
					gen.Emit(OpCodes.Call, property.GetGetMethod(true));
					gen.Emit(OpCodes.Box, property.PropertyType);
					gen.Emit(OpCodes.Ret);
					Func<object, object> access = (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
					CachedGetAccessors[key] = access;
					return (T)access(instance);
				}
			}
		}
		throw new Exception($"{fieldName} is not a field or property on {type}");
	}
}