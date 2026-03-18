// Credit RahulOfTheRamanEffect
// Sourced from - https://forum.unity3d.com/members/rahuloftheramaneffect.773241/
// Via Unity UI Extensions: https://bitbucket.org/UnityUIExtensions/unity-ui-extensions

using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.Components;

/// <summary>
/// Arranges child objects into a non-uniform grid, with fixed row heights and flexible column widths
/// </summary>
[AddComponentMenu("Layout/Extensions/Table Layout Group")]
internal class TableLayoutGroup : LayoutGroup
{
    public enum Corner
    {
        UpperLeft = 0,
        UpperRight = 1,
        LowerLeft = 2,
        LowerRight = 3,
    }

    [SerializeField]
    protected Corner startCorner = Corner.UpperLeft;

    /// <summary>
    /// The corner starting from which the cells should be arranged
    /// </summary>
    public Corner StartCorner
    {
        get { return startCorner; }
        set { SetProperty(ref startCorner, value); }
    }

    [SerializeField]
    protected float[] rowHeights = [32f];

    /// <summary>
    /// The heights of all the rows in the table
    /// </summary>
    public float[] RowHeights
    {
        get { return rowHeights; }
        set { SetProperty(ref rowHeights, value); }
    }

    [SerializeField]
    float minimumColumnWidth = 96f;

    /// <summary>
    /// The minimum width for any column in the table
    /// </summary>
    public float MinimumColumnWidth
    {
        get { return minimumColumnWidth; }
        set { SetProperty(ref minimumColumnWidth, value); }
    }

    [SerializeField]
    bool flexibleColumnWidth = true;

    /// <summary>
    /// Expand columns to fit the cell with the highest preferred width?
    /// </summary>
    public bool FlexibleColumnWidth
    {
        get { return flexibleColumnWidth; }
        set { SetProperty(ref flexibleColumnWidth, value); }
    }

    [SerializeField]
    float columnSpacing = 0f;

    /// <summary>
    /// The horizontal spacing between each cell in the table
    /// </summary>
    public float ColumnSpacing
    {
        get { return columnSpacing; }
        set { SetProperty(ref columnSpacing, value); }
    }

    [SerializeField]
    float rowSpacing = 0f;

    /// <summary>
    /// The vertical spacing between each row in the table
    /// </summary>
    public float RowSpacing
    {
        get { return rowSpacing; }
        set { SetProperty(ref rowSpacing, value); }
    }

    // Temporarily stores data generated during CalculateLayoutInputHorizontal for use in SetLayoutHorizontal
    private float[] preferredColumnWidths;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();

        if (rowHeights.Length == 0)
            rowHeights = [0f];

        int rowCount = rowHeights.Length;
        int columnCount = Mathf.CeilToInt(rectChildren.Count / (float)rowCount);

        preferredColumnWidths = new float[columnCount];

        float totalMinWidth = padding.horizontal;
        float totalPreferredWidth = padding.horizontal;

        if (columnCount > 1)
        {
            float widthFromSpacing = (columnCount - 1) * columnSpacing;
            totalMinWidth += widthFromSpacing;
            totalPreferredWidth += widthFromSpacing;
        }

        if (flexibleColumnWidth)
        {
            // Find the max value for minimum and preferred widths in each column
            for (int j = 0; j < columnCount; j++)
            {
                float maxMinimumWidthInColumn = minimumColumnWidth;
                float maxPreferredWidthInColumn = minimumColumnWidth;

                for (int i = 0; i < rowCount; i++)
                {
                    int childIndex = (i * columnCount) + j;

                    // Safeguard against tables with incomplete columns
                    if (childIndex >= rectChildren.Count)
                        break;

                    maxPreferredWidthInColumn = Mathf.Max(
                        LayoutUtility.GetPreferredWidth(rectChildren[childIndex]),
                        maxPreferredWidthInColumn
                    );
                    maxMinimumWidthInColumn = Mathf.Max(
                        LayoutUtility.GetMinWidth(rectChildren[childIndex]),
                        maxMinimumWidthInColumn
                    );
                }

                totalMinWidth += maxMinimumWidthInColumn;
                totalPreferredWidth += maxPreferredWidthInColumn;

                // Cache calculated column width for reuse in SetLayoutHorizontal()
                preferredColumnWidths[j] = maxPreferredWidthInColumn;
            }
        }
        else
        {
            // Use minimumColumnWidth for all columns
            for (int j = 0; j < columnCount; j++)
                preferredColumnWidths[j] = minimumColumnWidth;

            totalMinWidth += columnCount * minimumColumnWidth;
            totalPreferredWidth = totalMinWidth;
        }

        totalPreferredWidth = Mathf.Max(totalMinWidth, totalPreferredWidth);
        SetLayoutInputForAxis(totalMinWidth, totalPreferredWidth, 1, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        if (rowHeights.Length == 0)
            rowHeights = [0f];

        // We calculate the actual row count for cases where the number of children is fewer than the number of rows
        int actualRowCount = Mathf.Min(rectChildren.Count, rowHeights.Length);

        float verticalSize = padding.vertical;

        for (int i = 0; i < actualRowCount; i++)
        {
            verticalSize += rowHeights[i];
            verticalSize += rowSpacing;
        }

        if (actualRowCount > 0)
            verticalSize -= rowSpacing;

        SetLayoutInputForAxis(verticalSize, verticalSize, 0, 1);
    }

    public override void SetLayoutHorizontal()
    {
        if (rowHeights.Length == 0)
            rowHeights = [0f];

        int columnCount = preferredColumnWidths.Length;
        int cornerX = (int)startCorner % 2;

        float requiredSizeWithoutPadding = 0;
        for (int j = 0; j < columnCount; j++)
        {
            requiredSizeWithoutPadding += preferredColumnWidths[j];
            requiredSizeWithoutPadding += columnSpacing;
        }
        if (columnCount > 0)
            requiredSizeWithoutPadding -= columnSpacing;

        float startOffset = GetStartOffset(0, requiredSizeWithoutPadding);

        if (cornerX == 1)
            startOffset += requiredSizeWithoutPadding;

        float positionX = startOffset;

        for (int i = 0; i < rectChildren.Count; i++)
        {
            int currentColumnIndex = i % columnCount;

            // If it's the first cell in the row, reset positionX
            if (currentColumnIndex == 0)
                positionX = startOffset;

            if (cornerX == 1)
                positionX -= preferredColumnWidths[currentColumnIndex];

            SetChildAlongAxis(rectChildren[i], 0, positionX, preferredColumnWidths[currentColumnIndex]);

            if (cornerX == 1)
                positionX -= columnSpacing;
            else
                positionX += preferredColumnWidths[currentColumnIndex] + columnSpacing;
        }

        // Free memory
        preferredColumnWidths = null;
    }

    public override void SetLayoutVertical()
    {
        if (rowHeights.Length == 0)
            rowHeights = [0f];

        int rowCount = rowHeights.Length;
        int columnCount = Mathf.CeilToInt(rectChildren.Count / (float)rowCount);
        int cornerY = (int)startCorner / 2;

        // We calculate the actual row count for cases where the number of children is fewer than the number of rows
        int actualRowCount = Mathf.Min(rectChildren.Count, rowHeights.Length);

        float requiredSizeWithoutPadding = 0;
        for (int i = 0; i < actualRowCount; i++)
            requiredSizeWithoutPadding += rowHeights[i];

        if (actualRowCount > 1)
            requiredSizeWithoutPadding += (actualRowCount - 1) * rowSpacing;

        float startOffset = GetStartOffset(1, requiredSizeWithoutPadding);

        if (cornerY == 1)
            startOffset += requiredSizeWithoutPadding;

        float positionY = startOffset;

        for (int i = 0; i < actualRowCount; i++)
        {
            if (cornerY == 1)
                positionY -= rowHeights[i];

            for (int j = 0; j < columnCount; j++)
            {
                int childIndex = (i * columnCount) + j;

                // Safeguard against tables with incomplete rows
                if (childIndex >= rectChildren.Count)
                    break;

                SetChildAlongAxis(rectChildren[childIndex], 1, positionY, rowHeights[i]);
            }

            if (cornerY == 1)
                positionY -= rowSpacing;
            else
                positionY += rowHeights[i] + rowSpacing;
        }
    }
}
