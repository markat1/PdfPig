﻿namespace UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter
{
    using Content;
    using Core;
    using Geometry;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <inheritdoc />
    /// <summary>
    /// The Document Spectrum (Docstrum) algorithm is a bottom-up page segmentation technique based on nearest-neighbourhood 
    /// clustering of connected components extracted from the document. 
    /// This implementation leverages bounding boxes and does not exactly replicates the original algorithm.
    /// <para>See 'The document spectrum for page layout analysis.' by L. O'Gorman.</para>
    /// </summary>
    public class DocstrumBoundingBoxes : IPageSegmenter
    {
        /// <summary>
        /// Create an instance of Docstrum for bounding boxes page segmenter, <see cref="DocstrumBoundingBoxes"/>.
        /// </summary>
        public static DocstrumBoundingBoxes Instance { get; } = new DocstrumBoundingBoxes();

        /// <inheritdoc />
        /// <summary>
        /// Get the blocks.
        /// <para>Uses wlAngleLB = -30, wlAngleUB = 30, blAngleLB = -135, blAngleUB = -45, blMulti = 1.3.</para>
        /// </summary>
        /// <param name="words">The words to segment into <see cref="TextBlock"/>s.</param>
        /// <returns>The <see cref="TextBlock"/>s generated by the document spectrum method.</returns>
        public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<Word> words)
        {
            return GetBlocks(words, -1);
        }

        /// <summary>
        /// Get the blocks.
        /// <para>Uses wlAngleLB = -30, wlAngleUB = 30, blAngleLB = -135, blAngleUB = -45, blMulti = 1.3.</para>
        /// </summary>
        /// <param name="words">The words to segment into <see cref="TextBlock"/>s.</param>
        /// <param name="maxDegreeOfParallelism">Sets the maximum number of concurrent tasks enabled. 
        /// <para>A positive property value limits the number of concurrent operations to the set value. 
        /// If it is -1, there is no limit on the number of concurrently running operations.</para></param>
        /// <returns>The <see cref="TextBlock"/>s generated by the document spectrum method.</returns>
        public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<Word> words, int maxDegreeOfParallelism)
        {
            return GetBlocks(words, new AngleBounds(-30, 30), new AngleBounds(-135, -45), 1.3, maxDegreeOfParallelism);
        }

        /// <summary>
        /// Get the blocks. See original paper for more information.
        /// </summary>
        /// <param name="words">The words to segment into <see cref="TextBlock"/>s.</param>
        /// <param name="withinLine">Angle bounds for words to be considered on the same line.</param>
        /// <param name="betweenLine">Angle bounds for words to be considered on separate lines.</param>
        /// <param name="betweenLineMultiplier">Multiplier that gives the maximum perpendicular distance between 
        /// text lines for blocking. Maximum distance will be this number times the between-line 
        /// distance found by the analysis.</param>
        /// <returns>The <see cref="TextBlock"/>s generated by the document spectrum method.</returns>
        public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<Word> words, AngleBounds withinLine,
         AngleBounds betweenLine, double betweenLineMultiplier)
        {
            return GetBlocks(words, withinLine, betweenLine, betweenLineMultiplier, -1);
        }

        /// <summary>
        /// Get the blocks. See original paper for more information.
        /// </summary>
        /// <param name="words">The words to segment into <see cref="TextBlock"/>s.</param>
        /// <param name="withinLine">Angle bounds for words to be considered on the same line.</param>
        /// <param name="betweenLine">Angle bounds for words to be considered on separate lines.</param>
        /// <param name="betweenLineMultiplier">Multiplier that gives the maximum perpendicular distance between 
        /// text lines for blocking. Maximum distance will be this number times the between-line 
        /// distance found by the analysis.</param>
        /// <param name="maxDegreeOfParallelism">Sets the maximum number of concurrent tasks enabled. 
        /// <para>A positive property value limits the number of concurrent operations to the set value. 
        /// If it is -1, there is no limit on the number of concurrently running operations.</para></param>
        /// <returns>The <see cref="TextBlock"/>s generated by the document spectrum method.</returns>
        public IReadOnlyList<TextBlock> GetBlocks(IEnumerable<Word> words, AngleBounds withinLine,
            AngleBounds betweenLine, double betweenLineMultiplier, int maxDegreeOfParallelism)
        {
            if (words == null)
            {
                return EmptyArray<TextBlock>.Instance;
            }

            var wordsList = new List<Word>();

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                wordsList.Add(word);
            }

            if (wordsList.Count == 0)
            {
                return EmptyArray<TextBlock>.Instance;
            }

            var withinLineDistList = new ConcurrentBag<double>();
            var betweenLineDistList = new ConcurrentBag<double>();

            ParallelOptions parallelOptions = new ParallelOptions() { MaxDegreeOfParallelism = maxDegreeOfParallelism };

            // 1. Estimate within line and between line spacing
            KdTree<Word> kdTreeWL = new KdTree<Word>(wordsList, w => w.BoundingBox.BottomLeft);
            KdTree<Word> kdTreeBL = new KdTree<Word>(wordsList, w => w.BoundingBox.TopLeft);

            Parallel.For(0, wordsList.Count, parallelOptions, i =>
            {
                var word = wordsList[i];

                // Within-line distance
                var neighbourWL = kdTreeWL.FindNearestNeighbours(word, 2, w => w.BoundingBox.BottomRight, (p1, p2) => Distances.WeightedEuclidean(p1, p2, 0.5));
                foreach (var n in neighbourWL)
                {
                    if (withinLine.Contains(Distances.Angle(word.BoundingBox.BottomRight, n.Item1.BoundingBox.BottomLeft)))
                    {
                        withinLineDistList.Add(Distances.Horizontal(word.BoundingBox.BottomRight, n.Item1.BoundingBox.BottomLeft));
                    }
                }

                // Between-line distance
                var neighbourBL = kdTreeBL.FindNearestNeighbours(word, 2, w => w.BoundingBox.BottomLeft, (p1, p2) => Distances.WeightedEuclidean(p1, p2, 50));
                foreach (var n in neighbourBL)
                {
                    if (betweenLine.Contains(Distances.Angle(word.BoundingBox.Centroid, n.Item1.BoundingBox.Centroid)))
                    {
                        betweenLineDistList.Add(Distances.Vertical(word.BoundingBox.BottomLeft, n.Item1.BoundingBox.TopLeft));
                    }
                }
            });

            double? withinLineDistance = GetPeakAverageDistance(withinLineDistList);
            double? betweenLineDistance = GetPeakAverageDistance(betweenLineDistList);

            if (!withinLineDistance.HasValue || !betweenLineDistance.HasValue)
            {
                return new[] { new TextBlock(new[] { new TextLine(wordsList) }) };
            }

            // 2. Find lines of text
            double maxDistanceWithinLine = Math.Min(3 * withinLineDistance.Value, Math.Sqrt(2) * betweenLineDistance.Value);
            var lines = GetLines(wordsList, maxDistanceWithinLine, withinLine, maxDegreeOfParallelism).ToArray();

            // 3. Find blocks of text
            double maxDistanceBetweenLine = betweenLineMultiplier * betweenLineDistance.Value;
            var blocks = GetLinesGroups(lines, maxDistanceBetweenLine, maxDegreeOfParallelism).ToList();

            // 4. Merge overlapping blocks - might happen in certain conditions, e.g. justified text.
            for (var b = 0; b < blocks.Count; b++)
            {
                if (blocks[b] == null)
                {
                    continue;
                }

                // Merge all lines (words)
                blocks[b] = new TextBlock(GetLines(blocks[b].TextLines.SelectMany(l => l.Words).ToList(),
                    double.MaxValue, withinLine, maxDegreeOfParallelism).ToList());

                for (var c = 0; c < blocks.Count; c++)
                {
                    if (b == c || blocks[c] == null)
                    {
                        continue;
                    }

                    if (blocks[b].BoundingBox.IntersectsWith(blocks[c].BoundingBox))
                    {
                        // Merge
                        // 1. Merge all words
                        var mergedWords = new List<Word>(blocks[b].TextLines.SelectMany(l => l.Words));
                        mergedWords.AddRange(blocks[c].TextLines.SelectMany(l => l.Words));

                        // 2. Rebuild lines, using max distance = +Inf as we know all words will be in the
                        // same block. Filtering will still be done based on angle.
                        // Merge all lines (words) sharing same bottom (baseline)
                        var mergedLines = GetLines(mergedWords, double.MaxValue, withinLine, maxDegreeOfParallelism).ToList();
                        blocks[b] = new TextBlock(mergedLines.OrderByDescending(l => l.BoundingBox.Bottom).ToList());

                        // Remove
                        blocks[c] = null;
                    }
                }
            }

            return blocks.Where(b => b != null).ToList();
        }

        private static IEnumerable<TextLine> GetLines(List<Word> words, double maxDist, AngleBounds withinLine, int maxDegreeOfParallelism)
        {
            TextOrientation TextOrientation = words[0].TextOrientation;
            var groupedIndexes = Clustering.NearestNeighbours(words, 2, Distances.Euclidean,
                    (pivot, candidate) => maxDist,
                    pivot => pivot.BoundingBox.BottomRight, candidate => candidate.BoundingBox.BottomLeft,
                    pivot => true,
                    (pivot, candidate) => withinLine.Contains(Distances.Angle(pivot.BoundingBox.BottomRight, candidate.BoundingBox.BottomLeft)),
                    maxDegreeOfParallelism).ToList();

            Func<IEnumerable<Word>, IReadOnlyList<Word>> orderFunc = l => l.OrderBy(x => x.BoundingBox.Left).ToList();
            if (TextOrientation == TextOrientation.Rotate180)
            {
                orderFunc = l => l.OrderByDescending(x => x.BoundingBox.Right).ToList();
            }
            else if (TextOrientation == TextOrientation.Rotate90)
            {
                orderFunc = l => l.OrderByDescending(x => x.BoundingBox.Top).ToList();
            }
            else if (TextOrientation == TextOrientation.Rotate270)
            {
                orderFunc = l => l.OrderBy(x => x.BoundingBox.Bottom).ToList();
            }

            for (var a = 0; a < groupedIndexes.Count; a++)
            {
                yield return new TextLine(orderFunc(groupedIndexes[a].Select(i => words[i])));
            }
        }

        private static IEnumerable<TextBlock> GetLinesGroups(TextLine[] lines, double maxDist, int maxDegreeOfParallelism)
        {
            /**************************************************************************************************
             * We want to measure the distance between two lines using the following method:
             *  We check if two lines are overlapping horizontally.
             *  If they are overlapping, we compute the middle point (new X coordinate) of the overlapping area.
             *  We finally compute the Euclidean distance between these two middle points.
             *  If the two lines are not overlapping, the distance is set to the max distance.
             **************************************************************************************************/

            double euclidianOverlappingMiddleDistance(PdfLine l1, PdfLine l2)
            {
                var left = Math.Max(l1.Point1.X, l2.Point1.X);
                var d = (Math.Min(l1.Point2.X, l2.Point2.X) - left);

                if (d < 0) return double.MaxValue; // not overlapping -> max distance

                return Distances.Euclidean(
                    new PdfPoint(left + d / 2, l1.Point1.Y),
                    new PdfPoint(left + d / 2, l2.Point1.Y));
            }

            var groupedIndexes = Clustering.NearestNeighbours(lines,
                euclidianOverlappingMiddleDistance,
                (pivot, candidate) => maxDist,
                pivot => new PdfLine(pivot.BoundingBox.BottomLeft, pivot.BoundingBox.BottomRight),
                candidate => new PdfLine(candidate.BoundingBox.TopLeft, candidate.BoundingBox.TopRight),
                pivot => true, (pivot, candidate) => true,
                maxDegreeOfParallelism).ToList();

            for (int a = 0; a < groupedIndexes.Count; a++)
            {
                yield return new TextBlock(groupedIndexes[a].Select(i => lines[i]).ToList());
            }
        }

        /// <summary>
        /// Get the average distance value of the peak bucket of the histogram.
        /// </summary>
        /// <param name="distances">The set of distances to average.</param>
        private static double? GetPeakAverageDistance(IEnumerable<double> distances)
        {
            var buckets = new Dictionary<int, List<double>>();
            foreach (var distance in distances)
            {
                var floor = (int)distance;

                if (buckets.ContainsKey(floor))
                {
                    buckets[floor].Add(distance);
                }
                else
                {
                    buckets[floor] = new List<double> { distance };
                }
            }

            var best = default(List<double>);

            foreach (var bucket in buckets)
            {
                if (best == null || bucket.Value.Count > best.Count)
                {
                    best = bucket.Value;
                }
            }

            return best?.Average();
        }

        /// <summary>
        /// The bounds for the angle between two words for them to have a certain type of relationship.
        /// </summary>
        public struct AngleBounds
        {
            /// <summary>
            /// The lower bound in degrees.
            /// </summary>
            public double Lower { get; }

            /// <summary>
            /// The upper bound in degrees.
            /// </summary>
            public double Upper { get; }

            /// <summary>
            /// Create a new <see cref="AngleBounds"/>.
            /// </summary>
            public AngleBounds(double lowerBound, double upperBound)
            {
                Lower = lowerBound;
                Upper = upperBound;
            }

            /// <summary>
            /// Whether the bounds contain the angle.
            /// </summary>
            public bool Contains(double angle)
            {
                return angle >= Lower && angle <= Upper;
            }
        }
    }
}
