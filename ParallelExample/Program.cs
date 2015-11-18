using System;
using System.Diagnostics;
using ClusterExecutor;

namespace ParallelExample {
    internal class Program {
        private static void Main(string[] args) {
            Cluster.Init(typeof (Program));
            // var watch = Stopwatch.StartNew();

            Console.WriteLine(Method1() + Method2());

            //   watch.Stop();

            //   var elapsedMs = watch.ElapsedMilliseconds;
            //   Console.WriteLine("Time {0}", elapsedMs);
            Console.ReadKey();

            Cluster.Finish();
        }

        [PureFunction]
        public static double Method1() {
            var res = 0;
            for (var i = 0; i < 100; ++i) {
                Console.WriteLine("Method1");
                res++;
            }

            return res;
        }

        [PureFunction]
        public static double Method2() {
            var res = 0;
            for (var i = 0; i < 100; ++i) {
                Console.WriteLine("Method2");
                res++;
            }

            return res;
        }
    }
}