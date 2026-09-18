namespace JTCStamper.Core;

// The default is evaluated at use time, not cached at application startup.
public sealed class StampDateSelection(Func<DateOnly>? today = null)
{
    readonly Func<DateOnly> today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
    DateOnly? specified;
    public bool IsSpecified => specified.HasValue;
    public DateOnly Resolve() => specified ?? today();
    public void Specify(DateOnly date) => specified = date;
    public void UseToday() => specified = null;
}
