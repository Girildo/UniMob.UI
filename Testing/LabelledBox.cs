using System;
using UniMob.UI;
using UniMob.UI.Rendering;
using UniMob.UI.Widgets;

namespace UniMob.UI.Tests
{
    /// <summary>
    ///     A leaf widget that can label itself from its widget, from its state, from a live atom, or
    ///     not at all -- the cases <c>DiagnosticNode</c> has to tell apart.
    /// </summary>
    /// <remarks>
    ///     Stateful on purpose. A stateless widget could always supply a label; a stateful one could
    ///     not, which is the gap the channel exists to close, so a fixture built on
    ///     <c>StatelessWidget</c> would test the part that already worked.
    /// </remarks>
    public class LabelledBox : StatefulWidget
    {
        public string WidgetLabel { get; set; }

        /// <summary>When set, the state answers with this instead, which is the precedence rule.</summary>
        public string StateLabel { get; set; }

        /// <summary>A label free to read reactive state, since the reporter reads it under NoWatch.</summary>
        public Func<string> LiveLabel { get; set; }

        public override string GetDiagnosticInfo() =>
            this.LiveLabel != null ? this.LiveLabel() : this.WidgetLabel;

        public override State CreateState() => new LabelledBoxState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderConstrainedLeaf(state, LayoutConstraints.TightFor(0, 0));
    }

    public class LabelledBoxState : ViewState<LabelledBox>
    {
        // Never dereferenced -- see FixedSizeBoxState.View for why any reference is safe here.
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");

        public override string GetDiagnosticInfo() =>
            this.Widget.StateLabel ?? base.GetDiagnosticInfo();
    }

    /// <summary>A widget whose label throws, which is author code and therefore always possible.</summary>
    public class ThrowingLabelBox : StatefulWidget
    {
        public override string GetDiagnosticInfo() =>
            throw new InvalidOperationException("a label that throws");

        public override State CreateState() => new ThrowingLabelBoxState();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderConstrainedLeaf(state, LayoutConstraints.TightFor(0, 0));
    }

    public class ThrowingLabelBoxState : ViewState<ThrowingLabelBox>
    {
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");
    }

    /// <summary>Generic, so a rendered type name has arguments to expand.</summary>
    public class GenericBox<T> : StatefulWidget
    {
        public override State CreateState() => new GenericBoxState<T>();

        public override RenderObject CreateRenderObject(BuildContext context, IState state) =>
            new RenderConstrainedLeaf(state, LayoutConstraints.TightFor(0, 0));
    }

    public class GenericBoxState<T> : ViewState<GenericBox<T>>
    {
        public override WidgetViewReference View =>
            WidgetViewReference.Resource("Layout/UniMob.Text");
    }
}
