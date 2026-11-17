using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace tairasoul.unity.common.util.cecil;

static class NoFixCopyingUtil {
	public static MethodDefinition copyMethod(MethodDefinition origin) {
		MethodDefinition def = new(origin.Name, origin.Attributes, origin.ReturnType)
		{
			IsAbstract = origin.IsAbstract,
			IsAddOn = origin.IsAddOn,
			IsAssembly = origin.IsAssembly,
			IsCheckAccessOnOverride = origin.IsCheckAccessOnOverride,
			IsCompilerControlled = origin.IsCompilerControlled,
			IsFamily = origin.IsFamily,
			IsFamilyAndAssembly = origin.IsFamilyAndAssembly,
			IsFamilyOrAssembly = origin.IsFamilyOrAssembly,
			IsFinal = origin.IsFinal,
			IsFire = origin.IsFire,
			IsForwardRef = origin.IsForwardRef,
			IsGetter = origin.IsGetter,
			IsSetter = origin.IsSetter,
			IsPrivate = origin.IsPrivate,
			IsPublic = origin.IsPublic,
			IsIL = origin.IsIL,
			IsInternalCall = origin.IsInternalCall,
			IsManaged = origin.IsManaged,
			IsUnmanaged = origin.IsUnmanaged,
			IsUnmanagedExport = origin.IsUnmanagedExport,
			IsNative = origin.IsNative,
			IsNewSlot = origin.IsNewSlot,
			IsStatic = origin.IsStatic,
			IsSynchronized = origin.IsSynchronized,
			IsVirtual = origin.IsVirtual,
			AggressiveInlining = origin.AggressiveInlining,
			CallingConvention = origin.CallingConvention,
			ImplAttributes = origin.ImplAttributes,
			NoInlining = origin.NoInlining,
			NoOptimization = origin.NoOptimization,
			Attributes = origin.Attributes,
			DebugInformation = origin.DebugInformation,
			IsHideBySig = origin.IsHideBySig,
			IsOther = origin.IsOther,
			IsPInvokeImpl = origin.IsPInvokeImpl,
			IsPreserveSig = origin.IsPreserveSig,
			IsRemoveOn = origin.IsRemoveOn,
			IsReuseSlot = origin.IsReuseSlot,
			IsRuntime = origin.IsRuntime,
			IsRuntimeSpecialName = origin.IsRuntimeSpecialName,
			IsSpecialName = origin.IsSpecialName
		};
		return def;
	}

	public static TypeDefinition copyType(TypeDefinition type) {
		var clone = new TypeDefinition(type.Namespace, type.Name, type.Attributes, type.BaseType);
		clone.IsAbstract = type.IsAbstract;
		clone.IsAnsiClass = type.IsAnsiClass;
		clone.IsAutoClass = type.IsAutoClass;
		clone.IsAutoLayout = type.IsAutoLayout;
		clone.IsBeforeFieldInit = type.IsBeforeFieldInit;
		clone.IsClass = type.IsClass;
		clone.IsExplicitLayout = type.IsExplicitLayout;
		clone.IsImport = type.IsImport;
		clone.IsInterface = type.IsInterface;
		clone.IsNotPublic = type.IsNotPublic;
		clone.IsPublic = type.IsPublic;
		clone.IsRuntimeSpecialName = type.IsRuntimeSpecialName;
		clone.IsSealed = type.IsSealed;
		clone.IsSequentialLayout = type.IsSequentialLayout;
		clone.IsSerializable = type.IsSerializable;
		clone.IsSpecialName = type.IsSpecialName;
		clone.IsUnicodeClass = type.IsUnicodeClass;
		clone.IsWindowsRuntime = type.IsWindowsRuntime;
		clone.ClassSize = type.ClassSize;
		clone.PackingSize = type.PackingSize;
		return clone;
	}

	static readonly ConstructorInfo ctor = typeof(Instruction)
		.GetConstructor(
			BindingFlags.NonPublic | BindingFlags.Instance,
			null, [typeof(OpCode), typeof(object)], null
		);

	static Instruction instrClone(OpCode opcode, object operand) {
		return (Instruction)ctor.Invoke([opcode, operand]);
	}

	public static Instruction copyInstruction(Instruction instruction) {
		Instruction instr;
		if (instruction.Operand is Instruction instruct) instr = instrClone(instruction.OpCode, copyInstruction(instruct));
		else instr = instrClone(instruction.OpCode, instruction.Operand);
		return instr;
	}

	public static VariableDefinition copyVariable(VariableDefinition origin) {
		var clone = new VariableDefinition(origin.VariableType);
		return clone;
	}

	public static CustomAttribute copyAttribute(CustomAttribute origin) {
		var clone = new CustomAttribute(origin.Constructor);
		return clone;
	}

	public static CustomAttribute copyAttributeWithNew(CustomAttribute origin, MethodReference constrRef) {
		var clone = new CustomAttribute(constrRef);
		return clone;
	}
	
	public static CustomAttributeArgument copyArgument(CustomAttributeArgument origin) {
		var clone = new CustomAttributeArgument(origin.Type, origin.Value);
		return clone;
	}

	public static PropertyDefinition copyProperty(PropertyDefinition origin) {
		var clone = new PropertyDefinition(origin.Name, origin.Attributes, origin.PropertyType);
		clone.IsRuntimeSpecialName = origin.IsRuntimeSpecialName;
		clone.IsSpecialName = origin.IsSpecialName;
		return clone;
	}

	public static EventDefinition copyEvent(EventDefinition origin) {
		var clone = new EventDefinition(origin.Name, origin.Attributes, origin.EventType);
		clone.IsRuntimeSpecialName = origin.IsRuntimeSpecialName;
		clone.IsSpecialName = origin.IsSpecialName;
		return clone;
	}

	public static FieldDefinition copyField(FieldDefinition origin) {
		var clone = new FieldDefinition(origin.Name, origin.Attributes, origin.FieldType)
		{
			IsAssembly = origin.IsAssembly,
			IsCompilerControlled = origin.IsCompilerControlled,
			IsFamily = origin.IsFamily,
			IsFamilyAndAssembly = origin.IsFamilyAndAssembly,
			IsFamilyOrAssembly = origin.IsFamilyOrAssembly,
			IsPrivate = origin.IsPrivate,
			IsPublic = origin.IsPublic,
			IsStatic = origin.IsStatic,
		};
		clone.IsInitOnly = origin.IsInitOnly;
		clone.IsLiteral = origin.IsLiteral;
		clone.IsNotSerialized = origin.IsNotSerialized;
		clone.IsPInvokeImpl = origin.IsPInvokeImpl;
		clone.IsRuntimeSpecialName = origin.IsRuntimeSpecialName;
		clone.IsSpecialName = origin.IsSpecialName;
		clone.HasFieldRVA = origin.HasFieldRVA;
		clone.Constant = origin.Constant;
		clone.InitialValue = origin.InitialValue;
		if (origin.HasMarshalInfo) clone.MarshalInfo = origin.MarshalInfo;
		return clone;
	}

	public static InterfaceImplementation copyInterface(InterfaceImplementation origin) {
		var i = new InterfaceImplementation(origin.InterfaceType);
		return i;
	}

	public static ParameterDefinition copyParameter(ParameterDefinition origin) {
		var clone = new ParameterDefinition(origin.ParameterType)
		{
			Attributes = origin.Attributes,
			IsIn = origin.IsIn,
			IsLcid = origin.IsLcid,
			IsOptional = origin.IsOptional,
			IsOut = origin.IsOut
		};
		clone.Name = origin.Name;
		if (origin.HasConstant) clone.Constant = origin.Constant;
		return clone;
	}

	public static RequiredModifierType copyRmt(RequiredModifierType origin) {
		var clone = new RequiredModifierType(origin.ModifierType, origin.ElementType);
		return clone;
	}

	public static RequiredModifierType copyRmtNew(RequiredModifierType origin, TypeReference modifierType, TypeReference elementType) {
		var clone = new RequiredModifierType(modifierType, elementType);
		return clone;
	}

	public static GenericInstanceType copyGit(GenericInstanceType origin) {
		var clone = new GenericInstanceType(origin.ElementType);
		return clone;
	}

	public static GenericInstanceType copyGitWithNew(GenericInstanceType origin, TypeReference elem) {
		var clone = new GenericInstanceType(elem);
		return clone;
	}

	public static GenericInstanceMethod copyGim(GenericInstanceMethod origin) {
		var clone = new GenericInstanceMethod(origin.ElementMethod);
		return clone;
	}

	public static GenericInstanceMethod copyGimWithNew(GenericInstanceMethod origin, MethodReference elem) {
		var clone = new GenericInstanceMethod(elem);
		return clone;
	}

	public static MethodReturnType copyMrt(MethodReturnType origin) {
		var clone = new MethodReturnType(origin.Method);
		return clone;
	}

	public static GenericParameter copyGeneric(GenericParameter origin, IGenericParameterProvider newProvider) {
		var clone = new GenericParameter(origin.Name, newProvider);
		clone.Attributes = origin.Attributes;
		clone.HasReferenceTypeConstraint = origin.HasReferenceTypeConstraint;
		clone.HasNotNullableValueTypeConstraint = origin.HasNotNullableValueTypeConstraint;
		clone.HasDefaultConstructorConstraint = origin.HasDefaultConstructorConstraint;
		clone.AllowByRefLikeConstraint = origin.AllowByRefLikeConstraint;
		clone.IsNonVariant = origin.IsNonVariant;
		clone.IsCovariant = origin.IsCovariant;
		clone.IsContravariant = origin.IsContravariant;
		return clone;
	}
}