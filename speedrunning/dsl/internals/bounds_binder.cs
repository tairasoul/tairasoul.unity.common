using tairasoul.unity.common.speedrunning.dsl.compiler;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

unsafe class partialBound {
	public Bounds* bounds;
	public GameObject gameObject;
	public Coordinate size;
}

unsafe class fullBound {
	public Bounds* bounds;
	public GameObject gameObject;
}

public static unsafe class BoundsBinder {
	static List<fullBound> fullBounds = [];
	static List<partialBound> partialBounds = [];

	public static void BindPartial(Bounds* bounds, GameObject gameObject) {
		fullBounds.Add(new() {
			bounds = bounds,
			gameObject = gameObject
		});
	}

	public static void BindFull(Bounds* bounds, GameObject gameObject, Coordinate size) {
		partialBounds.Add(new() {
			bounds = bounds,
			gameObject = gameObject,
			size = size
		});
	}

	internal static void CheckUpdates() {
		for (int i = 0; i < fullBounds.Count; i++) {
			var b = fullBounds[i];
			b.bounds->center = b.gameObject.transform.position;
			b.bounds->size = b.gameObject.transform.lossyScale;
		}
		for (int i = 0; i < partialBounds.Count; i++) {
			var b = fullBounds[i];
			b.bounds->center = b.gameObject.transform.position;
		}
	}

	public static void Unbind(Bounds* bounds) {
		foreach (var b in fullBounds) {
			if (b.bounds == bounds) {
				fullBounds.Remove(b);
				goto end;
			}
		}
		foreach (var b in partialBounds) {
			if (b.bounds == bounds) {
				partialBounds.Remove(b);
				goto end;
			}
		}
		end:
		return;
	}
}