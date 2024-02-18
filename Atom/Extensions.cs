namespace Atom;

public static class Extensions
{
    public static ValueTask On<TSender, TEventArgs>(this AsyncEventHandler<TSender, TEventArgs>? handler, TSender sender, TEventArgs e)
        where TSender : class
        where TEventArgs : EventArgs
    {
        if (handler is null) return ValueTask.CompletedTask;
        return handler(sender, e);
    }
}