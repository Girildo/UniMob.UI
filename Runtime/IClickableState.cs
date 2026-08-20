namespace UniMob.UI
{
    internal interface IClickableState : ISingleChildLayoutState
    {
        bool Interactable { get; }
        void OnClick();
    }
}
