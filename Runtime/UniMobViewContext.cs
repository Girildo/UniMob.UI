using UniMob.UI.Internal;
using UniMob.UI.Internal.ViewLoaders;
using UnityEngine;

namespace UniMob.UI
{
    public static class UniMobViewContext
    {
        /// <summary>
        /// The sprite an image widget falls back to when it is given none. Null unless the
        /// application sets one, in which case such a widget draws nothing.
        /// </summary>
        public static Sprite? DefaultWhiteImage { get; set; }

        /// <summary>The view being rendered, or null outside a render scope.</summary>
        internal static IViewTreeElement? CurrentElement;

        // Assigned by Initialize below, which the runtime calls before any scene loads and so before
        // anything can ask for a view. A null here is a bootstrap failure, not a case to handle.
        public static IViewLoader Loader = null!;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Loader = new MultiViewLoader(
                new InternalViewLoader(),
                new PrefabViewLoader(),
                new BuiltinResourcesViewLoader(),
                new AddressableViewLoader()
            );
        }
    }
}
