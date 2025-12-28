using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using MainMenuSettings.Extensions;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

record AccessorCacheKey(Type type, string fieldName);

public static class AccessorUtil {
	static Dictionary<AccessorCacheKey, Func<object, object>> CachedAccessors = [];
	static Dictionary<string, GameObject> GameObjectCache = [];

	internal static bool RemoveElement(Type type, string fieldName) => CachedAccessors.Remove(new(type, fieldName));

	internal static void ClearCache()
	{
		CachedAccessors.Clear();
		GameObjectCache.Clear();
	}

	public static GameObject? FindGameObject(string path) {
		if (GameObjectCache.TryGetValue(path, out var go) && go != null)
			return go;
		string[] split = path.TrimStart('/').Split('/');
		GameObject current = GameObject.Find(split[0]);
		if (current == null) return null;
		for (int i = 1; i < split.Length; i++)
		{
			current = current.Find(split[i]);
			if (current == null) return null;
		}
		GameObjectCache[path] = current;
		return current;
	}

	public static T Get<T>(Type type, object instance, string fieldName) {
		AccessorCacheKey key = new(type, fieldName);
		if (CachedAccessors.TryGetValue(key, out var accessor)) {
			return (T)accessor(instance);
		}
		FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
		if (field != null) {
			if (field.IsStatic) {
				if (field.IsPublic) {
					Expression expression = Expression.Field(null, field);
					Func<object> access = Expression.Lambda<Func<object>>(expression).Compile();
					CachedAccessors[key] = (_) => access();
					return (T)access();
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(T),
						[type],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldsfld, field);
					gen.Emit(OpCodes.Ret);
					Func<object> access = (Func<object>)dm.CreateDelegate(typeof(Func<object>));
					CachedAccessors[key] = (_) => access();
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
					CachedAccessors[key] = access;
					return (T)access(instance);
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(T),
						[type],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldarg_1);
					gen.Emit(OpCodes.Castclass, type);
					gen.Emit(OpCodes.Ldfld, field);
					gen.Emit(OpCodes.Ret);
					Func<object, object> access = (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
					CachedAccessors[key] = access;
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
					CachedAccessors[key] = (_) => access();
					return (T)access();
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(T),
						[type],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Call, property.GetGetMethod(true));
					gen.Emit(OpCodes.Ret);
					Func<object> access = (Func<object>)dm.CreateDelegate(typeof(Func<object>));
					CachedAccessors[key] = (_) => access();
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
					CachedAccessors[key] = access;
					return (T)access(instance);
				}
				else {
					DynamicMethod dm = new(
						"",
						typeof(T),
						[type],
						type,
						true);
					ILGenerator gen = dm.GetILGenerator();
					gen.Emit(OpCodes.Ldarg_1);
					gen.Emit(OpCodes.Castclass, type);
					gen.Emit(OpCodes.Call, property.GetGetMethod(true));
					if (property.PropertyType.IsValueType)
							gen.Emit(OpCodes.Box, property.PropertyType);
					gen.Emit(OpCodes.Ret);
					Func<object, object> access = (Func<object, object>)dm.CreateDelegate(typeof(Func<object, object>));
					CachedAccessors[key] = access;
					return (T)access(instance);
				}
			}
		}
		throw new Exception();
	}
}