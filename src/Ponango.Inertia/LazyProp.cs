using System;

namespace Ponango.Inertia
{
    public class LazyProp
    {
        private readonly Func<object> _valueProvider;

        public LazyProp(Func<object> valueProvider)
        {
            _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
        }

        public object Invoke()
        {
            return _valueProvider();
        }
    }
}