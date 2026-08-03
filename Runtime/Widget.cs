using System;
using JetBrains.Annotations;
using UniMob.UI.Layout.Internal.RenderObjects;

namespace UniMob.UI
{
    // ReSharper disable once InconsistentNaming
    public interface Widget
    {
        Type Type { get; }

        Key Key { get; }

        State CreateState(StateProvider provider);
        State CreateState();

        /// <summary>
        /// Creates the lightweight RenderObject responsible for layout calculations.
        /// </summary>
        RenderObject CreateRenderObject(BuildContext context, IState state);

        /// <summary>
        /// A label describing <i>this</i> widget, printed beside its type name wherever the tree is
        /// described. Return <c>null</c> -- the default -- when the type name already says everything
        /// useful, which is most of the time.
        /// </summary>
        /// <remarks>
        /// The contract, because a label is read at the worst possible moment:
        /// <list type="bullet">
        ///     <item>
        ///         <description>
        ///             One line, no newlines. A console entry shows only its first line in the list.
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <description>
        ///             Describes the instance, not the type. The type name is already printed beside it,
        ///             so <c>"AppButton"</c> is noise and <c>"Add to cart"</c> is the point.
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <description>
        ///             May read reactive state freely. It is called inside <c>Atom.NoWatch</c>, so a live
        ///             value cannot drag whatever is reporting into a dependency on it.
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <description>
        ///             Must tolerate being called at a bad moment: mid-layout, before anything has been
        ///             laid out, and on a disposed state. Throwing degrades that one label rather than
        ///             the report, but it still costs the reader the thing they came for.
        ///         </description>
        ///     </item>
        ///     <item>
        ///         <description>
        ///             Costs nothing when healthy. It is a plain virtual, invoked only while a report is
        ///             actually being composed.
        ///         </description>
        ///     </item>
        /// </list>
        /// <para>
        ///     For a widget with a <see cref="State"/>, the state may override this with something only
        ///     it can see, and wins when it does.
        /// </para>
        /// </remarks>
        [CanBeNull]
        string GetDiagnosticInfo();
    }
}