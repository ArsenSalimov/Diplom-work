using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClusterExecutor {
   public class LazyImpl<T> {
        private readonly object[] _args;
        private readonly string _methodName;
        private readonly Func<string, object[], object> _factory;

        public LazyImpl(object[] args, string methodName, Func<string, object[], object> factory) {
            this._args = args;
            this._methodName = methodName;
            this._factory = factory;
        }

        public T Value => (T)_factory(_methodName, _args);

       public static implicit operator T(LazyImpl<T> lazy) {
           return default(T);
       }
    }
}