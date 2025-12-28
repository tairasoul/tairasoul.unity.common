using tairasoul.unity.common.speedrunning.dsl;
using tairasoul.unity.common.speedrunning.dsl.internals;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.runtime;

class RuntimeBehaviour : MonoBehaviour {
	internal bool IsActive = false;
	internal ISplitFile? activeFile;

	void Update() {
		if (!IsActive || activeFile == null) return;
		BoundsBinder.CheckUpdates();
		activeFile.CallCurrentSplit();
		if (activeFile.IsCompleted()) {
			IsActive = false;
		}
	}
}