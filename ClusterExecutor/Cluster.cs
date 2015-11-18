using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace ClusterExecutor {
    public class Cluster {
        private static readonly Dictionary<int, Task<object>> CalculatingFuncs = new Dictionary<int, Task<object>>();
        private static string assemblyName;

        public static void Init(Type type) {
            var path = type.Assembly.Location;
            assemblyName = type.Assembly.FullName;

            if (!path.Contains("_Shadow")) {
                var accemblyModifier = new AssemblyModifier();
                accemblyModifier.CreateShadowAssembly(path);
                RunShadowAssembly(path + "_Shadow");
            }
        }

        public static void Finish() {
            Environment.Exit(0);
        }

        private static void RunShadowAssembly(string path) {
            var domain = AppDomain.CreateDomain("ShadowDomain");
            domain.ExecuteAssembly(path);
        }

        public static object GetValueForFunc(string name, object[] args) {
            var task = CalculatingFuncs[args.GetHashCode() ^ name.GetHashCode()];
            return task.Result;
        }

        private static TypeDefinition getType(AssemblyDefinition assembly,string typeName) {
            TypeDefinition type = null;

            foreach (var module in assembly.Modules) {
                type = module.Types.First(t => t.FullName == typeName);
            }

            return type;
        }

        public static void CallRemoteMethod(string typeName,string methodName,object[] args) {
            var type = Type.GetType($"{typeName},{assemblyName}");
            var targetMethod = type.GetMethod(methodName);

            var task = new Task<object>(() => targetMethod.Invoke(null, args));
            CalculatingFuncs.Add(args.GetHashCode() ^ methodName.GetHashCode(), task);
            task.Start();
        }
    }
}