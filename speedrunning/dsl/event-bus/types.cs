using tairasoul.unity.common.events;
using UnityEngine;

namespace tairasoul.unity.common.speedrunning.dsl.eventbus;

public record DslId(string id) : EventID;
public record DslData(object[] args) : EventData;

public record DslFileCompleted() : EventID;
public record DslSplitCompleted() : EventID;
public record DslSplitCompletionData(string splitName) : EventData;

public record DslPlayerEnteredBounds() : EventID;
public record DslPlayerLeftBounds() : EventID;
public record DslBoundEntered(Bounds bounds) : EventData;
public record DslBoundLeft(Bounds bounds) : EventData;