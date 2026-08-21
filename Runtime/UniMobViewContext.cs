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

        private static IViewLoader? s_loader;

        /// <summary>Where a widget's view comes from.</summary>
        /// <remarks>
        ///     Built on first use, so resolving a view works outside play mode. The runtime hook below
        ///     only ever runs when play mode starts.
        /// </remarks>
        public static IViewLoader Loader
        {
            get => s_loader ??= CreateDefaultLoader();
            set => s_loader = value;
        }

        // Reassigned on entering play mode, so a loader installed by an editor tool or left by a test
        // does not outlive the session that installed it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            s_loader = CreateDefaultLoader();
        }

        private static IViewLoader CreateDefaultLoader() =>
            new MultiViewLoader(
                new InternalViewLoader(),
                new PrefabViewLoader(),
                new BuiltinResourcesViewLoader(),
                new AddressableViewLoader()
            );
    }
}
