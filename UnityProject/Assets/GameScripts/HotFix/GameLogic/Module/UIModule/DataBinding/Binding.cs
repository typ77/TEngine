using System;

namespace GameLogic.DataBinding
{
    internal sealed class Binding
    {
        private readonly Action _unsubscribe;

        public Binding(Action unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        public void Unsubscribe() => _unsubscribe();
    }
}
