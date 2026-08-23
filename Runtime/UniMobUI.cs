using System;
using UniMob.UI;
using UniMob.UI.Widgets;
using UnityEngine;

namespace UniMob.UI
{
    public static class UniMobUI
    {
        public static void RunApp(
            Lifetime lifetime,
            ViewPanel root,
            WidgetBuilder<Widget> builder,
            string? debugName = null
        )
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var rootContext = new BuildContext(null, null);

            var stateHolder = State.Create<Widget, IState>(
                lifetime,
                rootContext,
                ctx =>
                {
                    var child = builder.Invoke(ctx);
                    return new UniMobDeviceWidget(child, root.gameObject);
                }
            );

            IView view = root;
            lifetime.Register(() => view.ResetSource());

            // The builder wraps whatever it is given in a UniMobDeviceWidget, so the holder always
            // has a state.
            // debugName!: UniMob core defaults the parameter to null without annotating it nullable.
            Atom.Reaction(lifetime, () => root.Render(stateHolder.Value!), debugName: debugName!);
        }
    }
}
