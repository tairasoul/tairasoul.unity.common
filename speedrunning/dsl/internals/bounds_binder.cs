using tairasoul.unity.common.speedrunning.dsl.compiler;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.internals;

record struct fullBound(Bounds bounds, GameObject gameObject);
record struct partialBound(Bounds bounds, GameObject gameObject, Coordinate size);

public static class BoundsBinder {
	static List<fullBound> fullBounds = [];
	static List<partialBound> partialBounds = [];

	public static void BindPartial(Bounds bounds, GameObject gameObject) {
		fullBounds.Add(new(bounds, gameObject));
	}

	public static void BindFull(Bounds bounds, GameObject gameObject, Coordinate size) {
		partialBounds.Add(new(bounds, gameObject, size));
	}

	internal static void CheckUpdates() {
		for (int i = 0; i < fullBounds.Count; i++) {
			var b = fullBounds[i];
			Bounds bo = b.bounds;
			bo.center = b.gameObject.transform.position;
			bo.size = b.gameObject.transform.lossyScale;
			b.bounds = bo;
		}
		for (int i = 0; i < partialBounds.Count; i++) {
			var b = fullBounds[i];
			Bounds bo = b.bounds;
			bo.center = b.gameObject.transform.position;
			b.bounds = bo;
		}
	}

	public static void Unbind(Bounds bounds) {
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