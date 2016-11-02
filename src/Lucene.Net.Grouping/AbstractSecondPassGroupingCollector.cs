﻿using Lucene.Net.Index;
using Lucene.Net.Support;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lucene.Net.Search.Grouping
{
    /// <summary>
    /// SecondPassGroupingCollector is the second of two passes
    /// necessary to collect grouped docs.  This pass gathers the
    /// top N documents per top group computed from the
    /// first pass. Concrete subclasses define what a group is and how it
    /// is internally collected.
    /// <para>
    /// See {@link org.apache.lucene.search.grouping} for more
    /// details including a full code example.
    /// </para>
    /// @lucene.experimental
    /// </summary>
    /// <typeparam name="TGroupValue"></typeparam>
    public abstract class AbstractSecondPassGroupingCollector<TGroupValue> : Collector, IAbstractSecondPassGroupingCollector<TGroupValue>
    {
        protected readonly IDictionary<TGroupValue, AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue>> groupMap;
        private readonly int maxDocsPerGroup;
        protected AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue>[] groupDocs;
        private readonly IEnumerable<ISearchGroup<TGroupValue>> groups;
        private readonly Sort withinGroupSort;
        private readonly Sort groupSort;

        private int totalHitCount;
        private int totalGroupedHitCount;

        public AbstractSecondPassGroupingCollector(IEnumerable<ISearchGroup<TGroupValue>> groups, Sort groupSort, Sort withinGroupSort,
                                                   int maxDocsPerGroup, bool getScores, bool getMaxScores, bool fillSortFields)
        {

            //System.out.println("SP init");
            if (groups.Count() == 0)
            {
                throw new ArgumentException("no groups to collect (groups.size() is 0)");
            }

            this.groupSort = groupSort;
            this.withinGroupSort = withinGroupSort;
            this.groups = groups;
            this.maxDocsPerGroup = maxDocsPerGroup;
            groupMap = new HashMap<TGroupValue, AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue>>(groups.Count());

            foreach (SearchGroup<TGroupValue> group in groups)
            {
                //System.out.println("  prep group=" + (group.groupValue == null ? "null" : group.groupValue.utf8ToString()));
                //TopDocsCollector collector;
                ITopDocsCollector collector;
                if (withinGroupSort == null)
                {
                    // Sort by score
                    collector = TopScoreDocCollector.Create(maxDocsPerGroup, true);
                }
                else
                {
                    // Sort by fields
                    collector = TopFieldCollector.Create(withinGroupSort, maxDocsPerGroup, fillSortFields, getScores, getMaxScores, true);
                }
                groupMap[group.GroupValue] = new AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue>(group.GroupValue, collector);
            }
        }

        public override Scorer Scorer
        {
            set
            {
                foreach (AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue> group in groupMap.Values)
                {
                    group.collector.Scorer = value;
                }
            }
        }

        public override void Collect(int doc)
        {
            totalHitCount++;
            AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue> group = RetrieveGroup(doc);
            if (group != null)
            {
                totalGroupedHitCount++;
                group.collector.Collect(doc);
            }
        }

        /**
         * Returns the group the specified doc belongs to or <code>null</code> if no group could be retrieved.
         *
         * @param doc The specified doc
         * @return the group the specified doc belongs to or <code>null</code> if no group could be retrieved
         * @throws IOException If an I/O related error occurred
         */
        protected abstract AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue> RetrieveGroup(int doc);

        public override AtomicReaderContext NextReader
        {
            set
            {
                //System.out.println("SP.setNextReader");
                foreach (AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue> group in groupMap.Values)
                {
                    group.collector.NextReader = value;
                }
            }
        }

        public override bool AcceptsDocsOutOfOrder()
        {
            return false;
        }

        public ITopGroups<TGroupValue> GetTopGroups(int withinGroupOffset)
        {
            GroupDocs<TGroupValue>[] groupDocsResult = new GroupDocs<TGroupValue>[groups.Count()];

            int groupIDX = 0;
            float maxScore = float.MinValue;
            foreach (var group in groups)
            {
                AbstractSecondPassGroupingCollector.SearchGroupDocs<TGroupValue> groupDocs = groupMap.ContainsKey(group.GroupValue) ? groupMap[group.GroupValue] : null;
                TopDocs topDocs = groupDocs.collector.TopDocs(withinGroupOffset, maxDocsPerGroup);
                groupDocsResult[groupIDX++] = new GroupDocs<TGroupValue>(float.NaN,
                                                                              topDocs.MaxScore,
                                                                              topDocs.TotalHits,
                                                                              topDocs.ScoreDocs,
                                                                              groupDocs.groupValue,
                                                                              group.SortValues);
                maxScore = Math.Max(maxScore, topDocs.MaxScore);
            }

            return new TopGroups<TGroupValue>(groupSort.GetSort(),
                                                   withinGroupSort == null ? null : withinGroupSort.GetSort(),
                                                   totalHitCount, totalGroupedHitCount, groupDocsResult,
                                                   maxScore);
        }


        
    }

    public class AbstractSecondPassGroupingCollector
    {
        /// <summary>
        /// Don't allow creation
        /// </summary>
        private AbstractSecondPassGroupingCollector() { }

        // TODO: merge with SearchGroup or not?
        // ad: don't need to build a new hashmap
        // disad: blows up the size of SearchGroup if we need many of them, and couples implementations
        public class SearchGroupDocs<TGroupValue>
        {
            public readonly TGroupValue groupValue;
            //public readonly TopDocsCollector<?> collector;
            public readonly ITopDocsCollector collector;
            public SearchGroupDocs(TGroupValue groupValue, ITopDocsCollector /*TopDocsCollector<?>*/ collector)
            {
                this.groupValue = groupValue;
                this.collector = collector;
            }
        }
    }

    /// <summary>
    /// LUCENENET specific interface used to apply covariance to TGroupValue
    /// </summary>
    /// <typeparam name="TGroupValue"></typeparam>
    public interface IAbstractSecondPassGroupingCollector<out TGroupValue>
    {
        ITopGroups<TGroupValue> GetTopGroups(int withinGroupOffset);
    }
}
