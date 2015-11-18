using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace ClusterExecutor {
    internal class AssemblyModifier {
        public AssemblyDefinition CreateShadowAssembly(string assemblyPath) {
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var shadowAssemblyPath = assemblyPath + "_Shadow";

            ModifyAssembly(assembly);
            assembly.Write(shadowAssemblyPath);

            return assembly;
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
                            attr.AttributeType.FullName == typeof (PureFunctionAttribute).FullName);

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

          //  ProcessLazyCalls(assembly,currentModulte.Assembly,methodsToWatch);
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

            ilProcessor.Append(Instruction.Create(OpCodes.Ldstr, method.DeclaringType.FullName));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldstr, method.Name));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, paramsCount));
            ilProcessor.Append(Instruction.Create(OpCodes.Newarr, method.Module.Import(Type.GetType("System.Object"))));

            for (var i = 0; i < method.Parameters.Count; ++i) {
                if (i == 0)
                    ilProcessor.Append(Instruction.Create(OpCodes.Dup));

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
                    $"System.Func`3[{typeof (string).FullName},{typeof (object[]).FullName},{typeof (object)}]");
            var funcTypeConstructor = funcType?.GetConstructor(new Type[] {typeof (object), typeof (IntPtr)});
            var funcTypeConstructorRef = method.Module.Import(funcTypeConstructor);

            var lazyType = Type.GetType($"ClusterExecutor.LazyImpl`1[{retType.FullName}]");
            var lazyTypeConstructor = lazyType?.GetConstructor(new Type[] {typeof (object[]), typeof (string), funcType});
            var lazyTypeConstructorRef = method.Module.Import(lazyTypeConstructor);

            method.ReturnType = method.Module.Import(lazyType);

            var ilProcessor = method.Body.GetILProcessor();
            ilProcessor.Append(Instruction.Create(OpCodes.Ldstr, method.Name));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldnull));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldftn, getValueFuncRef));
            ilProcessor.Append(Instruction.Create(OpCodes.Newobj, funcTypeConstructorRef));
            ilProcessor.Append(Instruction.Create(OpCodes.Newobj, lazyTypeConstructorRef));
        }

        private void ProcessLazyCalls(AssemblyDefinition SourceAssembly,AssemblyDefinition currentAssebly, IList<MethodDefinition> lazyMethods) {
            foreach (var module in SourceAssembly.Modules) {
                foreach (var type in module.Types) {
                    foreach (var method in type.Methods) {
                        ProcessLazyCallsInMethod(method, lazyMethods);
                    }
                }
            }
        }

        private void ProcessLazyCallsInMethod(MethodDefinition method, IList<MethodDefinition> lazyMethods) {
            var body = method.Body;
            var ilProcessor = body.GetILProcessor();

            var stackEmulator = new Stack<int>();
            var listLazyVarsPos = new List<int>();

            for (var i = 0; i < body.Instructions.Count; i++) {
                var instrunction = body.Instructions[i];
                var opCode = instrunction.OpCode;
                var operand = instrunction.Operand;

                if (opCode == OpCodes.Call && lazyMethods.Contains(operand)) {
                    stackEmulator.Push(1);
                }
                else if (opCode == OpCodes.Stloc || opCode == OpCodes.Stloc_0 ||
                         opCode == OpCodes.Stloc_1 || opCode == OpCodes.Stloc_2 ||
                         opCode == OpCodes.Stloc_3 || opCode == OpCodes.Stloc_S && stackEmulator.Peek() == 1) {
                    stackEmulator.Pop();

                    var lazyVarIndex = GetVariableIndexForStloc(opCode, operand);
                    if (lazyVarIndex > 0 && !listLazyVarsPos.Contains(lazyVarIndex)) {
                        listLazyVarsPos.Add(lazyVarIndex);
                    }
                }
                else if (opCode == OpCodes.Ldloc || opCode == OpCodes.Ldloc_0 ||
                         opCode == OpCodes.Ldloc_1 || opCode == OpCodes.Ldloc_2 ||
                         opCode == OpCodes.Ldloc_3 || opCode == OpCodes.Ldloc_S) {

                    var varIndex = GetVariableIndexForLdloc(opCode, operand);
                    if (varIndex > 0 && listLazyVarsPos.Contains(varIndex)) {
                        stackEmulator.Push(1);
                    }
                    else {
                        stackEmulator.Push(0);
                    }
                }
                else if (opCode == OpCodes.Add || opCode == OpCodes.Sub) {
                    var arg1 = stackEmulator.Pop();
                    var arg2 = stackEmulator.Pop();

                    if (arg1 > 0) {
                        UnwrapLazyVariable(ilProcessor, body.Instructions[i - 2]);
                    }

                    if (arg2 > 0) {
                        UnwrapLazyVariable(ilProcessor, body.Instructions[i - 1]);
                    }

                    stackEmulator.Push(0);
                }
                else if (opCode == OpCodes.Ldstr) {
                    stackEmulator.Push(0);
                }
                else if (opCode == OpCodes.Pop) {
                    stackEmulator.Pop();
                }
            }
        }

        private void UnwrapLazyVariable(ILProcessor ilProcessor, Instruction targetInstructin) {
            ilProcessor.InsertAfter(targetInstructin,Instruction.Create(OpCodes.Blt));
        }

        private MethodReference getLazyVariableMethodRef() {
            return null;
        }

        private int GetVariableIndexForStloc(OpCode opCode,object operand) {
            if (opCode == OpCodes.Stloc || opCode == OpCodes.Stloc_S) {
                return ((VariableDefinition) operand).Index;
            } else if(opCode == OpCodes.Stloc_0) {
                return 0;
            } else if (opCode == OpCodes.Stloc_1) {
                return 1;
            } else if (opCode == OpCodes.Stloc_2) {
                return 2;
            } else if (opCode == OpCodes.Stloc_3) {
                return 3;
            } else {
                return -1;
            }
        }

        private int GetVariableIndexForLdloc(OpCode opCode, object operand) {
            if (opCode == OpCodes.Ldloc || opCode == OpCodes.Ldloc_S) {
                return ((VariableDefinition)operand).Index;
            } else if (opCode == OpCodes.Ldloc_0) {
                return 0;
            } else if (opCode == OpCodes.Ldloc_1) {
                return 1;
            } else if (opCode == OpCodes.Ldloc_2) {
                return 2;
            } else if (opCode == OpCodes.Ldloc_3) {
                return 3;
            } else {
                return -1;
            }
        }
    }
}