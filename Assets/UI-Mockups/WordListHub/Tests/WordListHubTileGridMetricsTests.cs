using NUnit.Framework;

namespace Words.UiMockups.WordListHub.Tests
{
    [TestFixture]
    public sealed class WordListHubTileGridMetricsTests
    {
        [TestCase(710f)]
        [TestCase(550f)]
        public void Calculate_UsesTwoPreferredTilesAndKeepsThemLeftAligned(float availableWidth)
        {
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(availableWidth, 2);

            Assert.That(metrics.ColumnCount, Is.EqualTo(2));
            Assert.That(metrics.RowCount, Is.EqualTo(1));
            Assert.That(metrics.TileSize, Is.EqualTo(270f));
            Assert.That(metrics.RowHeight, Is.EqualTo(270f));
            Assert.That(WordListHubTileGridMetrics.Gap, Is.EqualTo(5f));
            Assert.That(metrics.GetRowTileCount(0), Is.EqualTo(2));
            Assert.That(metrics.GetRowStartX(0), Is.EqualTo(0f));
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
            Assert.That(partial.GetRowStartX(0), Is.EqualTo(0f));
            Assert.That(partial.GetRowStartX(1), Is.EqualTo(0f));
            Assert.That(partial.ContentHeight, Is.EqualTo(545f));

            WordListHubTileGridMetrics fullRows = WordListHubTileGridMetrics.Calculate(710f, 5);
            Assert.That(fullRows.RowCount, Is.EqualTo(3));
            Assert.That(fullRows.GetRowTileCount(2), Is.EqualTo(1));
            Assert.That(fullRows.GetRowStartX(2), Is.EqualTo(0f));
            Assert.That(fullRows.ContentHeight, Is.EqualTo(820f));
        }

        [TestCase(1035f, 255f)]
        [TestCase(1050f, 258.75f)]
        public void Calculate_UsesFourColumnsWhenViewportFitsReadableTiles(float width, float expectedTileSize)
        {
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(width, 4);

            Assert.That(metrics.ColumnCount, Is.EqualTo(4));
            Assert.That(metrics.RowCount, Is.EqualTo(1));
            Assert.That(metrics.TileSize, Is.EqualTo(expectedTileSize).Within(0.001f));
            Assert.That(metrics.GetRowStartX(0), Is.EqualTo(0f).Within(0.001f));
            Assert.That(metrics.ContentHeight, Is.EqualTo(expectedTileSize).Within(0.001f));
        }

        [Test]
        public void Calculate_FallsBackToThreePreferredTilesWhenFourWouldBeTooNarrow()
        {
            WordListHubTileGridMetrics metrics = WordListHubTileGridMetrics.Calculate(980f, 4);

            Assert.That(metrics.ColumnCount, Is.EqualTo(3));
            Assert.That(metrics.TileSize, Is.EqualTo(270f));
            Assert.That(metrics.GetRowTileCount(0), Is.EqualTo(3));
            Assert.That(metrics.GetRowTileCount(1), Is.EqualTo(1));
            Assert.That(metrics.GetRowStartX(1), Is.EqualTo(0f));
        }

        [Test]
        public void Calculate_UsesOneShrinkingTileBelowMinimumReadableWidth()
        {
            WordListHubTileGridMetrics atMinimum = WordListHubTileGridMetrics.Calculate(250f, 2);
            WordListHubTileGridMetrics belowMinimum = WordListHubTileGridMetrics.Calculate(220f, 2);

            Assert.That(atMinimum.ColumnCount, Is.EqualTo(1));
            Assert.That(atMinimum.TileSize, Is.EqualTo(250f));
            Assert.That(belowMinimum.ColumnCount, Is.EqualTo(1));
            Assert.That(belowMinimum.TileSize, Is.EqualTo(220f));
        }
    }
}
