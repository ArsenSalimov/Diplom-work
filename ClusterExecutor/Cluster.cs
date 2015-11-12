using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ClusterExecutor {
    public class Cluster {
        private static readonly Dictionary<int, Task<object>> CalculatingFuncs = new Dictionary<int, Task<object>>();

        public static void Init(Type type) {
            var path = type.Assembly.Location;

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

        public static void CallRemoteMethod(object[] args) {
            var frame = new StackFrame(1);
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;
            var targetMethod = method.DeclaringType?.GetMethod(name + "_Shadow");

            var task = new Task<object>(() => targetMethod?.Invoke(null, args));
            CalculatingFuncs.Add(args.GetHashCode() ^ method.Name.GetHashCode(), task);
            task.Start();
        }
    }
}