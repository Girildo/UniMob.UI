using System;
using JetBrains.Annotations;

namespace UniMob.UI
{
    public abstract class Key : IEquatable<Key>
    {
        public abstract bool Equals(Key other);
        public sealed override bool Equals(object obj) => Equals(obj as Key);
        public override int GetHashCode() => throw new InvalidOperationException();

        public static Key Of([NotNull] object value) => new ObjectKey(value);

        public static bool operator ==(Key a, Key b) => a?.Equals(b) ?? ReferenceEquals(b, null);
        public static bool operator !=(Key a, Key b) => !a?.Equals(b) ?? !ReferenceEquals(b, null);
    }

    internal sealed class ObjectKey : Key, IEquatable<ObjectKey>
    {
        public object Value { get; }

        internal ObjectKey([NotNull] object value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override bool Equals(Key other) => Equals(other as ObjectKey);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"[Key: {Value}]";

        public bool Equals(ObjectKey other)
        {
            if (other == null)
                return false;

            if (ReferenceEquals(other.Value, Value))
                return true;

            return Value.Equals(other.Value);
        }
    }

    public sealed class GlobalKey<T> : GlobalKey, IEquatable<GlobalKey<T>>
        where T : class, IState
    {
        public override bool Equals(Key other) => Equals(other as GlobalKey<T>);
        public override int GetHashCode() => typeof(T).GetHashCode();
        public override string ToString() => $"[GlobalKey: {typeof(T)}]";

        public bool Equals(GlobalKey<T> other) => ReferenceEquals(other, this);

        public T CurrentState => UntypedCurrentState is T state ? state : default;
    }

    public abstract class GlobalKey : Key
    {
        // The binding is an atom so that "not bound yet" is observable: a geometry read made while
        // the key is unbound registers a dependency on the binding itself, and an observer created
        // before InflateWidget binds the key wakes when that happens instead of sleeping forever on
        // an empty dependency list. A plain property made exactly that shape a silent no-op.
        private readonly MutableAtom<State> _currentState = Atom.Value(default(State));

        // Untracked on purpose: the identity consumers (CurrentState, CurrentRawWidget,
        // CurrentContext, focus identity across the app) must not gain dependencies as a side
        // effect of the binding becoming observable.
        internal State UntypedCurrentState
        {
            get
            {
                using (Atom.NoWatch)
                {
                    return _currentState.Value;
                }
            }
            set => _currentState.Value = value;
        }

        /// <summary>
        ///     The bound state, read reactively: observers see the key bind and unbind.
        /// </summary>
        internal State TrackedCurrentState => _currentState.Value;

        public Widget CurrentRawWidget => UntypedCurrentState?.RawWidget;

        public BuildContext CurrentContext => UntypedCurrentState?.Context;
    }
}