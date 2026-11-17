using System.Collections.Generic;
using System.Linq;

namespace tairasoul.unity.common.repacker;

public class GenerationalQueue<T> {
	Stack<T> stack = [];
	List<T> immediate = [];
	List<T> pending = [];
	List<T> nextGen = [];
	public int TotalCount => stack.Count + immediate.Count + pending.Count + nextGen.Count;

	public bool TryDequeue(out T? item) {
		if (stack.Count > 0) {
			item = stack.Pop();
			return true;
		}
		item = default;
		return false;
	}

	public void EnqueueImmediate(T item) {
		immediate.Add(item);
	}

	public void Enqueue(T item) {
		pending.Add(item);
	}

	public void EnqueueNextGen(T item) {
		nextGen.Add(item);
	}

	public void CommitImmediate() {
		foreach (var item in immediate.Reverse<T>()) {
			stack.Push(item);
		}
		immediate.Clear();
	}

	public void Commit() {
		foreach (var item in pending.Reverse<T>()) {
			stack.Push(item);
		}
		pending.Clear();
		pending.AddRange(nextGen);
		nextGen.Clear();
	}
}