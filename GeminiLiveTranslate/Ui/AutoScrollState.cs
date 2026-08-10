namespace GeminiLiveTranslate.Ui;

public sealed class AutoScrollState
{
    private const double PositionTolerance = 0.5;

    public bool IsFollowing { get; private set; } = true;

    public void Pause() => IsFollowing = false;

    public void Resume() => IsFollowing = true;

    public void ObserveScroll(
        double verticalOffset,
        double scrollableHeight,
        double verticalChange,
        double extentHeightChange)
    {
        if (verticalChange < -PositionTolerance)
        {
            Pause();
            return;
        }

        var contentSizeUnchanged = Math.Abs(extentHeightChange) <= PositionTolerance;
        var isAtBottom = verticalOffset >= scrollableHeight - PositionTolerance;
        if (!IsFollowing && contentSizeUnchanged && isAtBottom)
        {
            Resume();
        }
    }
}
