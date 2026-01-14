/*
This program is free software: you can redistribute it and/or modify it under
the terms of the GNU Lesser General Public License as published by the Free
Software Foundation, either version 3 of the License, or (at your option) any
later version. This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Lesser General Public License
for more details. You should have received a copy of the GNU Lesser General
Public License along with this program. If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using ReactiveUI;

namespace deryaza.avalonia.controls;

public sealed class TreeFlattener<T> : IDisposable where T : class, INotifyPropertyChanged
{
    private readonly ObservableCollection<T> col = new();

    private readonly Func<T, ReadOnlyObservableCollection<T>> childrenSelector;
    private readonly Expression<Func<T, bool>> isHiddenExpr;
    private readonly Expression<Func<T, bool>> isExpandedExpr;
    private readonly Func<T, bool> isHiddenFunc;
    private readonly Func<T, bool> isExpandedFunc;

    private readonly Node root;

    public ReadOnlyObservableCollection<T> FlattenedCollection { get; }

    public TreeFlattener(
        ReadOnlyObservableCollection<T> source,
        Func<T, ReadOnlyObservableCollection<T>> children,
        Expression<Func<T, bool>> isHidden,
        Expression<Func<T, bool>> isExpanded)
    {
        FlattenedCollection = new(col);

        childrenSelector = children;

        isHiddenExpr = isHidden;
        isExpandedExpr = isExpanded;
        isHiddenFunc = isHidden.Compile();
        isExpandedFunc = isExpanded.Compile();

        root = new Node(
            tree: this,
            children: source,
            item: null,
            parentNode: null);

        root.Subscribe();

        InsertChildrenOf(root);
    }

    public void Dispose() => root.Dispose();

    public IEnumerable<T> GetAll()
    {
        foreach (var rootChildNode in root.ChildNodes)
        {
            foreach (var item in Walk(rootChildNode))
            {
                yield return item;
            }
        }

        yield break;

        IEnumerable<T> Walk(Node node)
        {
            Debug.Assert(node.Item != null);
            yield return node.Item;
            foreach (var nodeChildNode in node.ChildNodes)
            {
                foreach (var item in Walk(nodeChildNode))
                {
                    yield return item;
                }
            }
        }
    }

    private void InsertChildrenOf(Node parent)
    {
        if (!parent.AllowsChildrenNow)
        {
            foreach (var ch in parent.ChildNodes)
                ClearInsertedState(ch);
            return;
        }

        int insertAt = GetFirstChildFlatIndex(parent);
        foreach (var ch in parent.ChildNodes)
        {
            insertAt += InsertSubtreeIfVisible(ch, insertAt, parentAllowsChildren: true);
        }
    }

    private int InsertSubtreeIfVisible(Node node, int insertAt, bool parentAllowsChildren)
    {
        int oldCount = node.SubtreeVisibleCount;

        var items = new List<T>();
        int newCount = BuildVisibleItems(node, parentAllowsChildren, items);

        int delta = newCount - oldCount;
        if (delta == 0)
            return newCount;

        if (newCount > 0)
        {
            for (int i = 0; i < items.Count; i++)
                col.Insert(insertAt + i, items[i]);
        }

        AdjustAncestorCounts(node, delta);
        return newCount;
    }

    private int RemoveSubtreeIfInserted(Node node, int startIndex)
    {
        int count = node.SubtreeVisibleCount;
        if (count <= 0)
            return 0;

        for (int i = 0; i < count; i++)
            col.RemoveAt(startIndex);

        ClearInsertedState(node);
        AdjustAncestorCounts(node, -count);

        return count;
    }

    private void Collapse(Node node)
    {
        if (!node.SelfInFlattened)
            return;

        int descendants = node.SubtreeVisibleCount - 1;
        if (descendants <= 0)
            return;

        int start = GetFirstChildFlatIndex(node);
        for (int i = 0; i < descendants; i++)
            col.RemoveAt(start);

        foreach (var ch in node.ChildNodes)
            ClearInsertedState(ch);

        node.SubtreeVisibleCount = 1;
        AdjustAncestorCounts(node, -descendants);
    }

    private void Expand(Node node)
    {
        if (!node.SelfInFlattened)
            return;

        int insertAt = GetFirstChildFlatIndex(node);
        foreach (var ch in node.ChildNodes)
        {
            insertAt += InsertSubtreeIfVisible(ch, insertAt, parentAllowsChildren: true);
        }
    }

    private void ResetChildren(Node node, ReadOnlyObservableCollection<T> sender)
    {
        if (node.AllowsChildrenNow)
        {
            int start = GetFirstChildFlatIndex(node);
            int toRemove = node.VisibleDescendantsCount;
            if (toRemove > 0)
            {
                for (int i = 0; i < toRemove; i++)
                    col.RemoveAt(start);

                node.SubtreeVisibleCount -= toRemove;
                AdjustAncestorCounts(node, -toRemove);
            }
        }
        else
        {
            foreach (var ch in node.ChildNodes)
                ClearInsertedState(ch);
        }

        foreach (var ch in node.ChildNodes)
            ch.Dispose();
        node.ChildNodes.Clear();

        node.BuildChildNodesFrom(sender);

        InsertChildrenOf(node);
    }

    private void OnNodeVisibilityChanged(Node node, bool oldHidden, bool oldExpanded)
    {
        bool parentAllows = node.ParentNode?.AllowsChildrenNow ?? true;
        bool shouldBeVisible = parentAllows && !node.IsHiddenFlag;

        if (node.SelfInFlattened && !shouldBeVisible)
        {
            int start = GetNodeFlatStartIndex(node);
            RemoveSubtreeIfInserted(node, start);
            return;
        }

        if (!node.SelfInFlattened && shouldBeVisible)
        {
            int start = GetNodeFlatStartIndex(node);
            InsertSubtreeIfVisible(node, start, parentAllowsChildren: parentAllows);
            return;
        }

        if (node.SelfInFlattened)
        {
            if (oldExpanded && !node.IsExpandedFlag)
                Collapse(node);
            else if (!oldExpanded && node.IsExpandedFlag)
                Expand(node);
        }
    }

    private int BuildVisibleItems(Node node, bool parentAllowsChildren, List<T> output)
    {
        if (node.Item is null)
        {
            int total = 0;
            foreach (var ch in node.ChildNodes)
                total += BuildVisibleItems(ch, parentAllowsChildren: true, output);
            node.SubtreeVisibleCount = total;
            node.SelfInFlattened = false;
            return total;
        }

        bool selfVisible = parentAllowsChildren && !node.IsHiddenFlag;
        node.SelfInFlattened = selfVisible;

        if (!selfVisible)
        {
            ClearInsertedState(node);
            return 0;
        }

        output.Add(node.Item);
        int count = 1;

        bool canShowChildren = selfVisible && node.IsExpandedFlag;
        if (!canShowChildren)
        {
            foreach (var ch in node.ChildNodes)
                ClearInsertedState(ch);

            node.SubtreeVisibleCount = count;
            return count;
        }

        foreach (var ch in node.ChildNodes)
            count += BuildVisibleItems(ch, parentAllowsChildren: true, output);

        node.SubtreeVisibleCount = count;
        return count;
    }

    private void ClearInsertedState(Node node)
    {
        node.SelfInFlattened = false;
        node.SubtreeVisibleCount = 0;
        foreach (var ch in node.ChildNodes)
            ClearInsertedState(ch);
    }

    private void AdjustAncestorCounts(Node node, int delta)
    {
        var p = node.ParentNode;
        while (p != null)
        {
            p.SubtreeVisibleCount += delta;
            p = p.ParentNode;
        }
    }

    private int GetFirstChildFlatIndex(Node parent)
    {
        if (parent.Item is null)
            return 0;

        return GetNodeFlatStartIndex(parent) + 1;
    }

    private int GetNodeFlatStartIndex(Node node)
    {
        var parent = node.ParentNode;
        if (parent is null)
            return 0;

        int idxInParent = parent.ChildNodes.IndexOf(node);
        return GetChildFlatStartIndex(parent, idxInParent);
    }

    private int GetChildFlatStartIndex(Node parent, int childIndex)
    {
        int start = GetFirstChildFlatIndex(parent);
        for (int i = 0; i < childIndex; i++)
            start += parent.ChildNodes[i].SubtreeVisibleCount;
        return start;
    }

    private sealed class Node : IDisposable
    {
        private readonly TreeFlattener<T> tree;
        private ReadOnlyObservableCollection<T> children;
        private readonly CompositeDisposable disposables = new();

        public T? Item { get; }
        public Node? ParentNode { get; }

        public List<Node> ChildNodes { get; } = new();

        public bool SelfInFlattened { get; set; }
        public int SubtreeVisibleCount { get; set; }

        public bool IsHiddenFlag { get; private set; }
        public bool IsExpandedFlag { get; private set; }

        public bool AllowsChildrenNow =>
            Item is null || (SelfInFlattened && IsExpandedFlag);

        public int VisibleDescendantsCount =>
            Item is null ? SubtreeVisibleCount : Math.Max(0, SubtreeVisibleCount - (SelfInFlattened ? 1 : 0));

        public Node(
            TreeFlattener<T> tree,
            ReadOnlyObservableCollection<T> children,
            T? item,
            Node? parentNode)
        {
            this.tree = tree;
            this.children = children;

            Item = item;
            ParentNode = parentNode;
        }

        public void Dispose()
        {
            ((INotifyCollectionChanged)children).CollectionChanged -= OnChildrenChanged;

            foreach (var ch in ChildNodes)
                ch.Dispose();
            ChildNodes.Clear();

            disposables.Dispose();
        }

        public void Subscribe()
        {
            if (Item != null)
            {
                IsHiddenFlag = tree.isHiddenFunc(Item);
                IsExpandedFlag = tree.isExpandedFunc(Item);

                if (disposables.Count > 0)
                {
                    throw new();
                }

                Item.WhenAnyValue(tree.isHiddenExpr, tree.isExpandedExpr)
                    .DistinctUntilChanged()
                    .Skip(1)
                    .Subscribe(tuple =>
                    {
                        var (hidden, expanded) = tuple;
                        bool oldHidden = IsHiddenFlag;
                        bool oldExpanded = IsExpandedFlag;

                        IsHiddenFlag = hidden;
                        IsExpandedFlag = expanded;

                        tree.OnNodeVisibilityChanged(this, oldHidden, oldExpanded);
                    })
                    .DisposeWith(disposables);
            }

            ((INotifyCollectionChanged)children).CollectionChanged += OnChildrenChanged;

            OnChildrenChanged(children, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void BuildChildNodesFrom(ReadOnlyObservableCollection<T> sender)
        {
            for (int i = 0; i < sender.Count; i++)
            {
                var childItem = sender[i];
                var childChildren = tree.childrenSelector(childItem);

                var node = new Node(
                    tree: tree,
                    children: childChildren,
                    item: childItem,
                    parentNode: this);

                ChildNodes.Add(node);
                node.Subscribe();
            }
        }

        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var ro = (ReadOnlyObservableCollection<T>)sender!;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    tree.ResetChildren(this, ro);
                    break;

                case NotifyCollectionChangedAction.Add:
                    OnAdd(ro, e.NewStartingIndex, e.NewItems);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    OnRemove(ro, e.OldStartingIndex, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    OnReplace(ro, e.OldStartingIndex, e.NewItems, e.OldItems);
                    break;

                case NotifyCollectionChangedAction.Move:
                    OnMove(ro, e.OldStartingIndex, e.NewStartingIndex, e.NewItems);
                    break;
            }
        }

        private void OnAdd(ReadOnlyObservableCollection<T> sender, int newStartingIndex, IList? newItems)
        {
            if (newItems == null || newItems.Count == 0)
                return;

            for (int i = 0; i < newItems.Count; i++)
            {
                var childItem = (T)newItems[i]!;
                var childChildren = tree.childrenSelector(childItem);

                var node = new Node(
                    tree: tree,
                    children: childChildren,
                    item: childItem,
                    parentNode: this);

                ChildNodes.Insert(newStartingIndex + i, node);
                node.Subscribe();
            }

            if (!AllowsChildrenNow)
                return;

            int insertAt = tree.GetChildFlatStartIndex(this, newStartingIndex);
            for (int i = 0; i < newItems.Count; i++)
            {
                var node = ChildNodes[newStartingIndex + i];
                insertAt += tree.InsertSubtreeIfVisible(node, insertAt, parentAllowsChildren: true);
            }
        }

        private void OnRemove(ReadOnlyObservableCollection<T> sender, int oldStartingIndex, IList? oldItems)
        {
            if (oldItems == null || oldItems.Count == 0)
                return;

            if (AllowsChildrenNow)
            {
                int start = tree.GetChildFlatStartIndex(this, oldStartingIndex);

                for (int i = 0; i < oldItems.Count; i++)
                {
                    var node = ChildNodes[oldStartingIndex];
                    tree.RemoveSubtreeIfInserted(node, start);
                    node.Dispose();
                    ChildNodes.RemoveAt(oldStartingIndex);
                }
            }
            else
            {
                for (int i = 0; i < oldItems.Count; i++)
                {
                    var node = ChildNodes[oldStartingIndex];
                    node.Dispose();
                    ChildNodes.RemoveAt(oldStartingIndex);
                }
            }
        }

        private void OnReplace(ReadOnlyObservableCollection<T> sender, int index, IList? newItems, IList? oldItems)
        {
            if (oldItems != null && oldItems.Count > 0)
                OnRemove(sender, index, oldItems);

            if (newItems != null && newItems.Count > 0)
                OnAdd(sender, index, newItems);
        }

        private void OnMove(ReadOnlyObservableCollection<T> sender, int oldStartingIndex, int newStartingIndex, IList? newItems)
        {
            if (newItems == null || newItems.Count == 0)
                return;

            int count = newItems.Count;

            var moved = ChildNodes.GetRange(oldStartingIndex, count);

            if (AllowsChildrenNow)
            {
                int oldFlatStart = tree.GetChildFlatStartIndex(this, oldStartingIndex);
                int movedVisibleCount = moved.Sum(n => n.SubtreeVisibleCount);

                var buffer = new List<T>(movedVisibleCount);
                for (int i = 0; i < movedVisibleCount; i++)
                    buffer.Add(tree.col[oldFlatStart + i]);

                for (int i = 0; i < movedVisibleCount; i++)
                    tree.col.RemoveAt(oldFlatStart);

                ChildNodes.RemoveRange(oldStartingIndex, count);
                ChildNodes.InsertRange(newStartingIndex, moved);

                int newFlatStart = tree.GetChildFlatStartIndex(this, newStartingIndex);
                for (int i = 0; i < buffer.Count; i++)
                    tree.col.Insert(newFlatStart + i, buffer[i]);
            }
            else
            {
                ChildNodes.RemoveRange(oldStartingIndex, count);
                ChildNodes.InsertRange(newStartingIndex, moved);
            }
        }
    }
}