using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

public unsafe class BoundsPtrWrapper(Bounds* ptr)
{
	Bounds* ptr = ptr;

	public Bounds bounds {
		get {
			return *ptr;
		}
	}

	public static implicit operator Bounds(BoundsPtrWrapper wrapper) {
		return *wrapper.ptr;
	}

	public static bool operator ==(BoundsPtrWrapper lh, BoundsPtrWrapper rh) {
		return lh.ptr == rh.ptr;
	}

	public static bool operator !=(BoundsPtrWrapper lh, BoundsPtrWrapper rh) {
		return lh.ptr != rh.ptr;
	}

	public override int GetHashCode()
	{
		return (*ptr).GetHashCode();
	}

	public override bool Equals(object o) {
		if (o is BoundsPtrWrapper ptr)
			return ptr == this;
		return false;
	}
}