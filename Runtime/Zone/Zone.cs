using System;
using System.Collections.Generic;
using UniMob.UI.Diagnostics;
using UnityEngine;

namespace UniMob
{
    internal class Zone : MonoBehaviour
    {
        private readonly List<Action> _tickers = new List<Action>();

        private List<Action> _nextFrame = new List<Action>();
        private List<Action> _nextFrameExecuting = new List<Action>();

        // Assigned by Init below, which the runtime calls before any scene loads.
        public static Zone Current { get; set; } = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Init()
        {
            var go = new GameObject(nameof(Zone));
            var zone = go.AddComponent<Zone>();
            DontDestroyOnLoad(go);
            DontDestroyOnLoad(zone);

            Current = zone;
        }

        private void Update()
        {
            for (var i = _tickers.Count - 1; i >= 0; i--)
            {
                try
                {
                    _tickers[i].Invoke();
                }
                catch (Exception ex)
                {
                    UniMobError.Report(new UniMobFault(ex, "Ticker"));
                }
            }

            var toSwap = _nextFrame;
            _nextFrame = _nextFrameExecuting;
            _nextFrameExecuting = toSwap;

            foreach (var action in _nextFrameExecuting)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    UniMobError.Report(new UniMobFault(ex, "NextFrame"));
                }
            }

            _nextFrameExecuting.Clear();
        }

        public void AddTicker(Action action)
        {
            _tickers.Add(action);
        }

        public void RemoveTicker(Action action)
        {
            _tickers.Remove(action);
        }

        public void NextFrame(Action action)
        {
            _nextFrame.Add(action);
        }
    }
}
