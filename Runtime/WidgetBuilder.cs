namespace UniMob.UI
{
    public delegate TWidget WidgetBuilder<out TWidget>(BuildContext context)
        where TWidget : Widget;
}
