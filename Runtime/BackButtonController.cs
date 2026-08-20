using System;

namespace UniMob.UI
{
    public class BackButtonController
    {
        private Func<bool>? _handler;

        /// <summary>
        /// Registers callback for back button
        /// </summary>
        /// <param name="lifetime">Registration lifetime.</param>
        /// <param name="handler">Back Button handler.</param>
        public void RegisterHandler(Lifetime lifetime, Func<bool> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (lifetime.IsDisposed)
            {
                return;
            }

            _handler += handler;

            lifetime.Register(() => _handler -= handler);
        }

        /// <summary>
        /// Invokes back button handler.
        /// </summary>
        /// <returns>Returns true if any handler was invoked, otherwise false.</returns>
        public bool HandleBack()
        {
            return _handler?.Invoke() ?? false;
        }

        /// <summary>
        /// Creates BackButtonController and registers it to owner returned by lambda.
        /// </summary>
        /// <param name="func">Func that creates owner.</param>
        /// <returns>Owner created by func.</returns>
        /// <example>
        /// var route = BackButtonController.Create(bbc => new PageRouteBuilder(
        ///   new RouteSettings("example", RouteModalType.Fullscreen),
        ///   (context, controller, secondaryAnimation) => new ExampleWidget(bbc)
        /// ));
        /// </example>
        public static TBackActionOwner Create<TBackActionOwner>(
            Func<BackButtonController, TBackActionOwner> func
        )
            where TBackActionOwner : IBackActionOwner
        {
            if (func == null)
            {
                throw new ArgumentNullException(nameof(func));
            }

            var backButtonController = new BackButtonController();
            var route = func.Invoke(backButtonController);
            route.SetBackAction(backButtonController.HandleBack);
            return route;
        }
    }

    public interface IBackActionOwner
    {
        void SetBackAction(Func<bool> action);
    }
}
