using System;

namespace Ponango.Inertia
{
    /// <summary>
    /// A lazy-evaluated prop that is only included during partial reloads
    /// when explicitly requested. Deprecated in favor of <see cref="OptionalProp"/>.
    /// </summary>
    [Obsolete("Use OptionalProp instead. LazyProp will be removed in a future version.")]
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