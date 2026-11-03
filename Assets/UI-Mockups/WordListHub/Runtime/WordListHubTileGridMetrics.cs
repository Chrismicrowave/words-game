using System;

namespace Words.UiMockups.WordListHub
{
    /// <summary>
    /// Pure layout metrics for a centered grid of square Word List Hub tiles.
    /// Recalculate when the available viewport width or item count changes.
    /// </summary>
    public readonly struct WordListHubTileGridMetrics
    {
        public const int PreferredColumnCount = 4;
        public const float PreferredTileSize = 270f;
        public const float MinimumReadableTileSize = 250f;
        public const float Gap = 5f;

        private WordListHubTileGridMetrics(
            float availableWidth,
            int itemCount,
            int columnCount,
            float tileSize,
            int rowCount,
            float contentHeight)
        {
            AvailableWidth = availableWidth;
            ItemCount = itemCount;
            ColumnCount = columnCount;
            TileSize = tileSize;
            RowCount = rowCount;
            ContentHeight = contentHeight;
        }

        public float AvailableWidth { get; }
        public int ItemCount { get; }
        public int ColumnCount { get; }
        public int RowCount { get; }
        public float TileSize { get; }
        public float RowHeight => TileSize;
        public float ContentHeight { get; }

        /// <summary>
        /// Calculates grid dimensions for the current viewport width and item count.
        /// Uses up to four columns while keeping tiles at least 250 pixels wide when
        /// the viewport can fit them; otherwise it falls back to fewer columns.
        /// </summary>
        public static WordListHubTileGridMetrics Calculate(float availableWidth, int itemCount)
        {
            if (float.IsNaN(availableWidth) || float.IsInfinity(availableWidth))
                throw new ArgumentOutOfRangeException(nameof(availableWidth), "Width must be finite.");

            float width = Math.Max(0f, availableWidth);
            int columnsAtReadableSize = (int)Math.Floor(
                ((double)width + Gap) / (MinimumReadableTileSize + Gap));
            int columns = Math.Min(PreferredColumnCount, Math.Max(1, columnsAtReadableSize));
            float widthPerTile = (width - (columns - 1) * Gap) / columns;
            float tileSize = Math.Min(PreferredTileSize, Math.Max(0f, widthPerTile));
            int normalizedItemCount = Math.Max(0, itemCount);
            int rows = normalizedItemCount == 0
                ? 0
                : 1 + (normalizedItemCount - 1) / columns;
            float contentHeight = rows == 0
                ? 0f
                : rows * tileSize + (rows - 1) * Gap;

            return new WordListHubTileGridMetrics(
                width,
                normalizedItemCount,
                columns,
                tileSize,
                rows,
                contentHeight);
        }

        /// <summary>Returns how many tiles occupy the zero-based row.</summary>
        public int GetRowTileCount(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= RowCount)
                throw new ArgumentOutOfRangeException(nameof(rowIndex));

            int remainingItems = ItemCount - rowIndex * ColumnCount;
            return Math.Min(ColumnCount, remainingItems);
        }

        /// <summary>Returns the row's centered left edge, measured from the viewport's left edge.</summary>
        public float GetRowStartX(int rowIndex)
        {
            int rowTileCount = GetRowTileCount(rowIndex);
            float rowWidth = rowTileCount * TileSize + Math.Max(0, rowTileCount - 1) * Gap;
            return (AvailableWidth - rowWidth) * 0.5f;
        }
    }
}
