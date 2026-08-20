using System.Collections.Generic;
using UnityEngine;

namespace UniMob.UI.Internal.ViewLoaders
{
    public class PrefabViewLoader : IViewLoader
    {
        private readonly Dictionary<GameObject, IView> _viewPrefabCache =
            new Dictionary<GameObject, IView>();

        public IView? LoadViewPrefab(WidgetViewReference viewReference)
        {
            if (viewReference.Type != WidgetViewReferenceType.Prefab)
            {
                return null;
            }

            var prefab = viewReference.Prefab;

            // Ahead of the cache lookup: a null key throws out of the dictionary, so checking after
            // it meant the friendly message below was unreachable for the case it describes.
            if (prefab == null)
            {
                Debug.LogError("A Prefab view reference carries no prefab.");
                return null;
            }

            if (_viewPrefabCache.TryGetValue(prefab, out var view))
            {
                return view;
            }

            view = prefab.GetComponent<IView>();
            if (view == null)
            {
                Debug.LogError(
                    $"Failed to get IView from prefab '{prefab.name}'. Missing view component?"
                );
                return null;
            }

            _viewPrefabCache.Add(prefab, view);

            return view;
        }
    }
}
