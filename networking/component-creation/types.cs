using System;
using System.Threading.Tasks;
using tairasoul.unity.common.networking.sync;

namespace tairasoul.unity.common.networking.components;

interface IComponentCreator {
	public Task<BaseOwnedSyncComponent> RequestCreation(Func<CreationData, bool> predicate);
}