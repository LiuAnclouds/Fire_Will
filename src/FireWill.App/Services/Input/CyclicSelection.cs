namespace FireWill.App.Services.Input;

internal static class CyclicSelection
{
    public static int NextIndex(int currentIndex, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return currentIndex < 0 || currentIndex >= count - 1
            ? 0
            : currentIndex + 1;
    }
}
