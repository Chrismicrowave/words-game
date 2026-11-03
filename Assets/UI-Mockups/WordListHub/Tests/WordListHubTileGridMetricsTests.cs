using NUnit.Framework;

namespace Words.UiMockups.WordListHub.Tests
{
    [TestFixture]
    public sealed class WordListHubTileGridMetricsTests
    {
        [TestCase(710f, 82.5f)]
        [TestCase(550f, 2.5f)]
        public void Calculate_UsesTwoPreferredTilesAndCentersFullRow(float availableWidth, float expectedStartX)
        {
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(availableWidth, 2);

            Assert.That(metrics.ColumnCount, Is.EqualTo(2));
            Assert.That(metrics.RowCount, Is.EqualTo(1));
            Assert.That(metrics.TileSize, Is.EqualTo(270f));
            Assert.That(metrics.RowHeight, Is.EqualTo(270f));
            Assert.That(WordListHubTileGridMetrics.Gap, Is.EqualTo(5f));
            Assert.That(metrics.GetRowTileCount(0), Is.EqualTo(2));
            Assert.That(metrics.GetRowStartX(0), Is.EqualTo(expectedStartX).Within(0.001f));
            Assert.That(metrics.ContentHeight, Is.EqualTo(270f));
        }

        [Test]
        public void Calculate_ClampsNarrowViewportToOneTile()
        {
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(220f, 3);

            Assert.That(metrics.ColumnCount, Is.EqualTo(1));
            Assert.That(metrics.TileSize, Is.EqualTo(220f));
            Assert.That(metrics.RowCount, Is.EqualTo(3));
            Assert.That(metrics.GetRowStartX(0), Is.EqualTo(0f));
            Assert.That(metrics.ContentHeight, Is.EqualTo(670f));
        }

        [Test]
        public void Calculate_HandlesEmptyAndPartialRowsAndComputesContentHeight()
        {
            WordListHubTileGridMetrics empty = WordListHubTileGridMetrics.Calculate(710f, 0);
            Assert.That(empty.RowCount, Is.EqualTo(0));
            Assert.That(empty.ContentHeight, Is.EqualTo(0f));

            WordListHubTileGridMetrics partial = WordListHubTileGridMetrics.Calculate(710f, 3);
            Assert.That(partial.RowCount, Is.EqualTo(2));
            Assert.That(partial.GetRowTileCount(0), Is.EqualTo(2));
            Assert.That(partial.GetRowTileCount(1), Is.EqualTo(1));
            Assert.That(partial.GetRowStartX(0), Is.EqualTo(82.5f).Within(0.001f));
            Assert.That(partial.GetRowStartX(1), Is.EqualTo(220f).Within(0.001f));
            Assert.That(partial.ContentHeight, Is.EqualTo(545f));

            WordListHubTileGridMetrics fullRows = WordListHubTileGridMetrics.Calculate(710f, 5);
            Assert.That(fullRows.RowCount, Is.EqualTo(3));
            Assert.That(fullRows.GetRowTileCount(2), Is.EqualTo(1));
            Assert.That(fullRows.ContentHeight, Is.EqualTo(820f));
        }
    }
}
