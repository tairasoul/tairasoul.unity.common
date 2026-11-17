using System;
using Mono.Cecil;

namespace tairasoul.unity.common.util.cecil;

public static class CopyingUtil {
	public static class Raw {
		public static MethodDefinition copyMethod(MethodDefinition origin, ModuleDefinition module) {
			MethodDefinition def = new(origin.Name, origin.Attributes, module.ImportReference(origin.ReturnType))
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
				DebugInformation = origin.DebugInformation
			};
			return def;
		}

		public static PropertyDefinition copyPropertyDef(PropertyDefinition origin) {
			var clone = new PropertyDefinition(origin.Name, origin.Attributes, origin.PropertyType);
			return clone;
		}

		public static FieldDefinition copyField(FieldDefinition origin, ModuleDefinition module, string? rename = null) {
			var clone = new FieldDefinition(rename ?? origin.Name, origin.Attributes, module.ImportReference(origin.FieldType))
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
			if (origin.HasConstant) clone.Constant = origin.Constant;
			clone.InitialValue = origin.InitialValue;
			if (origin.HasMarshalInfo) clone.MarshalInfo = origin.MarshalInfo;
			return clone;
		}

		public static TypeDefinition copyType(TypeDefinition origin, ModuleDefinition target) {
			var clone = new TypeDefinition(origin.Namespace, origin.Name, origin.Attributes, target.ImportReference(origin.BaseType));
			return clone;
		}

		public static InterfaceImplementation copyInterface(InterfaceImplementation origin, ModuleDefinition targetModule) {
			var i = new InterfaceImplementation(targetModule.ImportReference(origin.InterfaceType));
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
			if (origin.HasConstant) clone.Constant = origin.Constant;
			return clone;
		}
	}

	public static MethodDefinition copyMethod(MethodDefinition origin, TypeDefinition target) {
		MethodDefinition def = new(origin.Name, origin.Attributes, target.Module.ImportReference(origin.ReturnType))
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
			DebugInformation = origin.DebugInformation
		};
		foreach (var attr in origin.CustomAttributes) 
			def.CustomAttributes.Add(attr);
		foreach (var gen in origin.GenericParameters) def.GenericParameters.Add(gen);
		foreach (var pam in origin.Parameters) def.Parameters.Add(pam);
		foreach (var ov in origin.Overrides) def.Overrides.Add(ov);
		if (origin.HasBody)
		{
			foreach (var instr in origin.Body.Instructions) def.Body.Instructions.Add(instr);
			foreach (var variable in origin.Body.Variables) def.Body.Variables.Add(variable);
		}
		target.Methods.Add(def);
		return def;
	}

	public static PropertyDefinition copyPropertyDef(PropertyDefinition origin) {
		var clone = new PropertyDefinition(origin.Name, origin.Attributes, origin.PropertyType);
		foreach (var attr in origin.CustomAttributes) clone.CustomAttributes.Add(attr);
		return clone;
	}

	public static PropertyDefinition copyProperty(PropertyDefinition origin, TypeDefinition target) {
		var clone = new PropertyDefinition(origin.Name, origin.Attributes, target.Module.ImportReference(origin.PropertyType));
		foreach (var attr in origin.CustomAttributes) clone.CustomAttributes.Add(attr);
		MethodDefinition? getterExisting = null;
		MethodDefinition? setterExisting = null;
		foreach (var method in target.Methods) {
			if (method.Name == "get_" + origin.Name) getterExisting = method;
			if (method.Name == "set_" + origin.Name) setterExisting = method;
		}
		if (getterExisting == null) {
			var getter = copyMethod(origin.GetMethod, target);
			clone.GetMethod = getter;
		}
		else {
			clone.GetMethod = getterExisting;
		}
		if (setterExisting == null)
		{
			var setter = copyMethod(origin.SetMethod, target);
			clone.SetMethod = setter;
		}
		else {
			clone.SetMethod = setterExisting;
		}
		return clone;
	}

	public static FieldDefinition copyField(FieldDefinition origin, TypeDefinition target, string? rename = null) {
		var clone = new FieldDefinition(rename ?? origin.Name, origin.Attributes, target.Module.ImportReference(origin.FieldType))
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
		foreach (var attr in origin.CustomAttributes) clone.CustomAttributes.Add(attr);
		if (origin.HasConstant) clone.Constant = origin.Constant;
		clone.InitialValue = origin.InitialValue;
		if (origin.HasMarshalInfo) clone.MarshalInfo = origin.MarshalInfo;
		target.Fields.Add(clone);
		return clone;
	}

	public static TypeDefinition copyType(TypeDefinition origin, ModuleDefinition target, Action<FieldDefinition, FieldDefinition> fieldCopied, Action<MethodDefinition, MethodDefinition> methodCopied, Action<PropertyDefinition, PropertyDefinition> propertyCopied, Action<TypeDefinition, TypeDefinition> typeCopied) {
		var clone = new TypeDefinition(origin.Namespace, origin.Name, origin.Attributes, origin.BaseType);
		clone.ClassSize = origin.ClassSize;
		foreach (var attr in origin.CustomAttributes) clone.CustomAttributes.Add(attr);
		if (origin.DeclaringType != null) clone.DeclaringType = copyType(origin.DeclaringType, target, fieldCopied, methodCopied, propertyCopied, typeCopied);
		foreach (var ev in origin.Events) clone.Events.Add(ev);
		foreach (var field in origin.Fields) {
			var copied = copyField(field, clone);
			fieldCopied(field, copied);
		}
		foreach (var method in origin.Methods) {
			var copied = copyMethod(method, clone);
			methodCopied(method, copied);
		}
		foreach (var prop in origin.Properties) {
			var copied = copyProperty(prop, clone);
			propertyCopied(prop, copied);
		}
		target.Types.Add(clone);
		typeCopied(origin, clone);
		return clone;
	}

	public static InterfaceImplementation copyInterface(InterfaceImplementation origin, ModuleDefinition targetModule) {
		var i = new InterfaceImplementation(targetModule.ImportReference(origin.InterfaceType));
		foreach (var attr in origin.CustomAttributes) i.CustomAttributes.Add(attr);
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
		if (origin.HasConstant) clone.Constant = origin.Constant;
		return clone;
	}
}