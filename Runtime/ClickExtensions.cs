using System;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UniMob.UI
{
    public static class ClickExtensions
    {
        public static void Click(this Button button, Func<Action> call)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));

            Bind(button.onClick, call);
        }

        public static void Click(this Button button, Action call)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));

            Bind(button.onClick, call);
        }

        public static void Bind(this UnityEvent unityEvent, Func<Action> call)
        {
            if (unityEvent == null)
                throw new ArgumentNullException(nameof(unityEvent));
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            void Listener()
            {
                using (Atom.NoWatch)
                {
                    var handler = call.Invoke();
                    handler?.Invoke();
                }
            }

            unityEvent.AddListener(Listener);
        }

        public static void Bind(this UnityEvent unityEvent, Action call)
        {
            if (unityEvent == null)
                throw new ArgumentNullException(nameof(unityEvent));
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            void Listener()
            {
                using (Atom.NoWatch)
                {
                    call.Invoke();
                }
            }

            unityEvent.AddListener(Listener);
        }

        public static void Bind<T>(this UnityEvent<T> unityEvent, Func<Action<T>> call)
        {
            if (unityEvent == null)
                throw new ArgumentNullException(nameof(unityEvent));
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            void Listener(T value)
            {
                using (Atom.NoWatch)
                {
                    var handler = call.Invoke();
                    handler?.Invoke(value);
                }
            }

            unityEvent.AddListener(Listener);
        }

        public static void Bind<T>(this UnityEvent<T> unityEvent, Action<T> call)
        {
            if (unityEvent == null)
                throw new ArgumentNullException(nameof(unityEvent));
            if (call == null)
                throw new ArgumentNullException(nameof(call));

            void Listener(T value)
            {
                using (Atom.NoWatch)
                {
                    call.Invoke(value);
                }
            }

            unityEvent.AddListener(Listener);
        }
    }
}
