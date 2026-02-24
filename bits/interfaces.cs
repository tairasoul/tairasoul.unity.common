using System;
using System.Threading.Tasks;

namespace tairasoul.unity.common.bits;

#if BITWRITING_INCLUDE_SYNC
public interface ISerializable {
	public void Serialize(BitWriter writer);
}
#endif

#if BITREADING_INCLUDE_SYNC
public interface IDeserializable<T> {
	public T Deserialize(BitReader reader);
}
#endif

#if BITWRITING_INCLUDE_ASYNC
public interface ISerializableAsync {
	public Task Serialize(BitWriterAsync writer);
}
#endif

#if BITREADING_INCLUDE_ASYNC
public interface IDeserializableAsync<T> {
	public Task<T> Deserialize(BitReaderAsync reader);
}
#endif