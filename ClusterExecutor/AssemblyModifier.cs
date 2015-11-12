using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace ClusterExecutor {
    class AssemblyModifier {
        public void CreateShadowAssembly(string assemblyPath) {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var shadowAssemblyPath = assemblyPath + "_Shadow";

            ModifyAssembly(assembly);
            assembly.Write(shadowAssemblyPath);
        }

        private void ModifyAssembly(AssemblyDefinition assembly) {
            var module = assembly.MainModule;
            var currentModulte = ModuleDefinition.ReadModule(Assembly.GetExecutingAssembly().Location);

            var callRemoteMethod =
                currentModulte.Types.First(t => t.FullName == "ClusterExecutor.Cluster")
                    .Methods.First(m => m.Name == "CallRemoteMethod");
            var methodRef = module.Import(callRemoteMethod);

            var methodsToWatch = new List<MethodDefinition>();

            foreach (var type in module.Types) {
                var methodsToAdd = new List<MethodDefinition>();
                foreach (var method in type.Methods) {
                    var contains = method.CustomAttributes.Any(
                        attr =>
                            attr.AttributeType.FullName == typeof(PureFunctionAttribute).FullName);

                    if (contains) {
                        methodsToAdd.Add(CreateStaticClone(method));
                        ModifyMethod(method, methodRef, currentModulte);

                        methodsToWatch.Add(method);
                    }
                }

                foreach (var method in methodsToAdd) {
                    type.Methods.Add(method);
                }
            }

            FixReturnVariables(module, methodsToWatch);
        }


        private void FixReturnVariables(ModuleDefinition module, IList<MethodDefinition> methods) {
            foreach (var type in module.Types) {
                foreach (var method in type.Methods) {
                    for (var i = 0; i < method.Body.Instructions.Count; ++i) {
                        var instruction = method.Body.Instructions[i];
                        if (instruction.OpCode == OpCodes.Call && methods.Contains(instruction.Operand)) {
                            var returnType = ((MethodDefinition) instruction.Operand).ReturnType;
                            if (returnType.Name == "Void" || !instruction.Next.ToString().Contains("stloc")) {
                                continue;
                            }
                     
                            var lazyType = Type.GetType(((MethodDefinition)instruction.Operand).ReturnType.FullName.Replace('<', '[').Replace('>', ']'));
                            var getMethod = lazyType.GetMethod("get_Value");
                            var getMethodRef = module.Import(getMethod);

                            method.Body.GetILProcessor()
                                .InsertAfter(instruction, Instruction.Create(OpCodes.Callvirt, getMethodRef));
                        }
                    }
                }
            }
        }

        private MethodDefinition CreateStaticClone(MethodDefinition method) {
            var staticMethod = new MethodDefinition(method.Name + "_Shadow",
                MethodAttributes.Static | MethodAttributes.Public, method.ReturnType);

            foreach (var instruction in method.Body.Instructions) {
                staticMethod.Body.Instructions.Add(instruction);
            }
            foreach (var variable in method.Body.Variables) {
                staticMethod.Body.Variables.Add(variable);
            }
            foreach (var param in method.Parameters) {
                staticMethod.Parameters.Add(param);
            }

            staticMethod.ReturnType = method.ReturnType;
            staticMethod.Body.InitLocals = true;
            staticMethod.Body.MaxStackSize = method.Body.MaxStackSize;

            return staticMethod;
        }

        private void ModifyMethod(MethodDefinition method, MethodReference cllRemoteRef,
            ModuleDefinition currentModule) {
            var ilProcessor = method.Body.GetILProcessor();
            var paramsCount = method.Parameters.Count;

            ilProcessor.Body.Instructions.Clear();
            ilProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, paramsCount));
            ilProcessor.Append(Instruction.Create(OpCodes.Newarr, method.Module.Import(Type.GetType("System.Object"))));
            ilProcessor.Append(Instruction.Create(OpCodes.Dup));

            for (var i = 0; i < method.Parameters.Count; ++i) {
                ilProcessor.Append(Instruction.Create(OpCodes.Dup));
                var param = method.Parameters[i];

                ilProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, i));
                ilProcessor.Append(Instruction.Create(OpCodes.Ldarg, param));


                if (param.ParameterType.IsValueType) {
                    ilProcessor.Append(Instruction.Create(OpCodes.Box, param.ParameterType));
                }

                ilProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
            }

            ilProcessor.Append(Instruction.Create(OpCodes.Call, cllRemoteRef));

            if (method.ReturnType.FullName != "System.Void") {
                AddLazyReturnType(method, currentModule);
            }

            ilProcessor.Append(Instruction.Create(OpCodes.Ret));
        }

        private void AddLazyReturnType(MethodDefinition method, ModuleDefinition currentModule) {
            var retType = method.ReturnType;

            var getValueFunc = currentModule.Types.First(t => t.FullName == "ClusterExecutor.Cluster")
                .Methods.First(m => m.Name == "GetValueForFunc");
            var getValueFuncRef = method.Module.Import(getValueFunc);

            method.Body.MaxStackSize = 20;

            var funcType =
                Type.GetType(
                    $"System.Func`3[{typeof(string).FullName},{typeof(object[]).FullName},{typeof(object)}]");
            var funcTypeConstructor = funcType?.GetConstructor(new Type[] { typeof(object), typeof(IntPtr) });
            var funcTypeConstructorRef = method.Module.Import(funcTypeConstructor);

            var lazyType = Type.GetType($"ClusterExecutor.LazyImpl`1[{retType.FullName}]");
            var lazyTypeConstructor = lazyType?.GetConstructor(new Type[] { typeof(object[]), typeof(string), funcType });
            var lazyTypeConstructorRef = method.Module.Import(lazyTypeConstructor);

            method.ReturnType = method.Module.Import(lazyType);

            var ilProcessor = method.Body.GetILProcessor();
            ilProcessor.Append(Instruction.Create(OpCodes.Ldstr, method.Name));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldnull));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldftn, getValueFuncRef));
            ilProcessor.Append(Instruction.Create(OpCodes.Newobj, funcTypeConstructorRef));
            ilProcessor.Append(Instruction.Create(OpCodes.Newobj, lazyTypeConstructorRef));
        }
    }
}
