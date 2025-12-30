
using tairasoul.unity.common.events;
using tairasoul.unity.common.speedrunning.dsl;
using tairasoul.unity.common.speedrunning.dsl.config;
using tairasoul.unity.common.speedrunning.dsl.eventbus;
using tairasoul.unity.common.speedrunning.dsl.internals;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.runtime;

class RuntimeBehaviour : MonoBehaviour {
	internal bool IsActive = false;
	internal SplitFileProxy? activeFile;

	void Update() {
		if (!IsActive || activeFile == null) return;
		BoundsBinder.CheckUpdates();
		DslCompilationConfig.BoundsRegistryClass.CheckBounds();
		activeFile.CallCurrentSplit();
		if (activeFile.IsCompleted()) {
			activeFile.Reset();
			EventBus.Send(new DslFileCompleted(), null);
			IsActive = false;
		}
	}
}