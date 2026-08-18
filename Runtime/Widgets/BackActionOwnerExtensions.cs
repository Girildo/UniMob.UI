using System;
using JetBrains.Annotations;

namespace UniMob.UI.Widgets
{
    public static class BackActionOwnerExtensions
    {
        /// <summary>
        ///     Makes the back button ask the topmost route to pop, carrying <see cref="PopRequest.Back"/>.
        ///     Back is chrome: it does not own the route, so it asks rather than pops.
        /// </summary>
        [PublicAPI]
        public static TBackActionOwner WithPopOnBack<TBackActionOwner>(this TBackActionOwner owner,
            NavigatorState navigatorState, Func<bool> filter = null)
            where TBackActionOwner : IBackActionOwner
        {
            bool HandleBack()
            {
                if (filter == null || filter.Invoke())
                {
                    if (navigatorState.NavigationStack.Count > 0)
                    {
                        _ = navigatorState.RequestPop(navigatorState.TopmostRoute, PopRequest.Back);
                    }

                    return true;
                }

                return false;
            }

            owner.SetBackAction(HandleBack);

            return owner;
        }
    }
}
