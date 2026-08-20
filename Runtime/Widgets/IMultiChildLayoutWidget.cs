using System.Collections.Generic;

namespace UniMob.UI.Widgets
{
    public interface IMultiChildLayoutWidget
    {
        /// <summary>
        /// The list of child widgets that this layout widget will arrange.
        /// </summary>
        List<Widget> Children { get; }
    }
}
