using FireWill.App.Services.Input;

namespace FireWill.App.Tests;

public sealed class CyclicSelectionTests
{
    [Theory]
    [InlineData(-1, 5, 0)]
    [InlineData(0, 5, 1)]
    [InlineData(3, 5, 4)]
    [InlineData(4, 5, 0)]
    [InlineData(99, 5, 0)]
    public void NextIndex_AdvancesAndWraps(
        int currentIndex,
        int count,
        int expected)
    {
        Assert.Equal(expected, CyclicSelection.NextIndex(currentIndex, count));
    }

    [Fact]
    public void NextIndex_EmptyCollectionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyclicSelection.NextIndex(currentIndex: -1, count: 0));
    }
}
