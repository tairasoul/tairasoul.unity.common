using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace tairasoul.unity.common.util;

public unsafe struct UnmanagedNullable<T> where T : unmanaged {
	public bool HasValue;
	public T Value;
	public UnmanagedNullable(T? data) {
		if (data.HasValue) {
			HasValue = true;
			Value = data.Value;
		}
		else {
			HasValue = false;
			CustomUnsafe.SkipInit(out Value);
		}
	}

	public UnmanagedNullable(T data) {
		HasValue = true;
		Value = data;
	}

	public static implicit operator UnmanagedNullable<T>(T value) => new(value);
	public static implicit operator UnmanagedNullable<T>(T? value) => new(value);
	public static implicit operator T?(UnmanagedNullable<T> value) => value.HasValue ? value.Value : null;
}