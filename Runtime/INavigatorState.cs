namespace UniMob.UI
{
    public interface INavigatorState : IViewState
    {
        IState[] Screens { get; }
    }
}
