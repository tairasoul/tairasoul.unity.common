// todo

// using System;
// using System.Collections.Generic;
// using System.Runtime.CompilerServices;
// using Mono.Cecil;
// using Mono.Cecil.Cil;

// namespace tairasoul.unity.common.util.cecil;

// struct AsyncInstruction(Instruction instruction, bool isAwait) {
// 	public Instruction instruction = instruction;
// 	public bool isAwait = isAwait;
// }

// public class AsyncMethodBuilder {
// 	List<AsyncInstruction> instructions;
// 	MethodDefinition primaryMethod;
// 	TypeDefinition stateMachine;
// 	TypeSystem typeSystem;

// 	public AsyncMethodBuilder(MethodDefinition primary) {
// 		primaryMethod = primary;
// 		instructions = [];
// 		stateMachine = new TypeDefinition(primary.DeclaringType.Namespace, $"<{primary.Name}>__stateMachine", TypeAttributes.NestedPrivate);
// 		stateMachine.IsValueType = true;
// 		typeSystem = primary.Module.TypeSystem;
// 	}

// 	public void Finish() {
// 		int currentState = 0;
// 		FieldDefinition stateField = new("<>1__state", FieldAttributes.Public, typeSystem.Int32);
// 		stateMachine.Fields.Add(stateField);
// 		MethodDefinition moveNext = new("MoveNext", MethodAttributes.Private, typeSystem.Void);
// 		Dictionary<int, List<AsyncInstruction>> stateInstructions = [];
// 		foreach (var instr in instructions) {
// 			int currentInstr = currentState;
// 			if (instr.isAwait) currentInstr = currentState++;
// 			if (stateInstructions.TryGetValue(currentInstr, out var instrs)) {
// 				instrs.Add(instr);
// 			}
// 			else {
// 				instrs = [];
// 				instrs.Add(instr);
// 				stateInstructions.Add(currentInstr, instrs);
// 			}
// 		}
// 		foreach (var kv in stateInstructions) {
// 			var state = kv.Key;
// 			var instrs = kv.Value;
			
// 		}
// 		primaryMethod.DeclaringType.NestedTypes.Add(stateMachine);
// 		var asyncStateMac = primaryMethod.Module.ImportReference(typeof(AsyncStateMachineAttribute));
// 		var res = asyncStateMac.Resolve();
// 		MethodDefinition? mainConstructor = null;
// 		foreach (var m in res.Methods) {
// 			if (m.IsConstructor) {
// 				mainConstructor = m;
// 				break;
// 			}
// 		}
// 		if (mainConstructor == null) throw new NullReferenceException();
// 		var asmAttr = new CustomAttribute(mainConstructor);
// 		var typeArg = new CustomAttributeArgument(asyncStateMac, stateMachine);
// 		primaryMethod.CustomAttributes.Add(asmAttr);
// 	}

// 	public void AddInstruction(Instruction instr) {
// 		instructions.Add(new(instr, false));
// 	}

// 	public void AddField(FieldDefinition definition) {
// 		stateMachine.Fields.Add(definition);
// 	}

// 	public void AddInstructionAwait(Instruction instr) {
// 		instructions.Add(new(instr, true));
// 	}
// }