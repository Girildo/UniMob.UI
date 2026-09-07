using TMPro;

namespace UniMob.UI
{
    /// <summary>
    ///     The text component <see cref="UniMob.UI.Widgets.Text"/> paints with.
    /// </summary>
    /// <remarks>
    ///     A named subclass rather than TextMeshProUGUI itself, so a view carrying one is
    ///     recognisable as this package's, and so RequireComponent can name it.
    /// </remarks>
    public class UniMobTextMeshProBehaviour : TextMeshProUGUI
    {
        /// <summary>
        ///     Whether TextMeshPro intends to regenerate this text and has not done so yet.
        /// </summary>
        /// <remarks>
        ///     TMP's own condition, from <c>OnPreRenderCanvas</c>, and it takes a subclass to ask it:
        ///     the layout half is protected and has no public accessor. Both halves are needed, because
        ///     the two ways a text goes stale raise different ones -- a property write raises
        ///     <c>havePropertiesChanged</c>, while a resize raises only the layout flag, from
        ///     <c>OnRectTransformDimensionsChange</c>. A rotation is the second kind, so anything asking
        ///     only about the first would not see it coming.
        /// </remarks>
        internal bool WantsRegeneration => m_havePropertiesChanged || m_isLayoutDirty;

        /// <summary>
        ///     Stops drawing this text and discards its geometry, so that nothing that re-uploads the
        ///     meshes later can draw the previous string.
        /// </summary>
        /// <remarks>
        ///     TextMeshPro's own <c>ClearMesh</c> only detaches the meshes from their canvas renderers;
        ///     the meshes keep the previous string's glyphs. Its per-frame scale update then hands them
        ///     straight back: a canvas scale change of more than 20% re-uploads the meshes as they are,
        ///     guarded only by the parse buffer starting with a terminator -- which a styled string
        ///     never does, because it starts with the style's opening tag, empty or not. That is how a
        ///     rotation brought back the words a label had been emptied of. Zeroing the geometry here
        ///     leaves that path nothing to draw.
        /// </remarks>
        public override void ClearMesh()
        {
            base.ClearMesh();
            m_textInfo?.ClearMeshInfo(true);
        }
    }
}
