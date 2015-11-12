using System;
using ClusterExecutor;

namespace ParallelExample {
    internal class Program {
        private static void Main(string[] args) {
            Cluster.Init(typeof (Program));

            double a = Method1();
            double b = Method2();
            double c = Method3();

            Console.WriteLine(a + b);
            Console.ReadKey();
            Console.WriteLine(a + b + c);
            Console.ReadKey();
            Cluster.Finish();
        }

        [PureFunction]
        public static double Method1() {
            var res = 0;
            for (var i = 0; i < 1000; ++i) {
               Console.WriteLine("Method1");
                res++;
            }

            return res;
        }
        [PureFunction]
        public static double Method2() {
            var res = 0;
            for (var i = 0; i < 1000; ++i) {
                Console.WriteLine("Method2");
                res++;
            }

            return res;
        }

        [PureFunction]
        public static double Method3() {
            var res = 0;
            for (var i = 0; i < 1000; ++i) {
                Console.WriteLine("Method3");
                res++;
            }

            return res;
        }
    }
}