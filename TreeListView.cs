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

#pragma warning disable SA1509
#pragma warning disable SA1508
#pragma warning disable SA1507
#pragma warning disable SA1402
#pragma warning disable SA1203
#pragma warning disable SA1310
#pragma warning disable SA1203
#pragma warning disable SA1203
#pragma warning disable SX1309
#pragma warning disable SA1502
#pragma warning disable SA1310
#pragma warning disable SA1129
#pragma warning disable SA1407
#pragma warning disable SA1503
#pragma warning disable SA1214
#pragma warning disable SA1401
#pragma warning disable SA1028
#pragma warning disable SA1512
#pragma warning disable SA1516
#pragma warning disable SA1400
#pragma warning disable SA1201
#pragma warning disable SA1116
#pragma warning disable SA1202

// ReSharper disable EnforceIfStatementBraces
// ReSharper disable EnforceForeachStatementBraces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Styling;
using Avalonia.Utilities;
using DynamicData;
using DynamicData.Binding;

namespace deryaza.avalonia.controls;

public class TreeListView : Grid
{
    public static readonly StyledProperty<object?> SelectedItemExProperty = AvaloniaProperty.Register<TreeListView, object?>(nameof(SelectedItemEx));
    public static readonly StyledProperty<GridView?> ViewProperty = AvaloniaProperty.Register<TreeListView, GridView?>(nameof(View));
    public static readonly StyledProperty<bool> ShowRowDetailsProperty = AvaloniaProperty.Register<TreeListView, bool>(nameof(ShowRowDetails));
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<TreeListView, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<string?> ChildrenPropertyNameProperty = AvaloniaProperty.Register<TreeListView, string?>(nameof(ChildrenPropertyName));
    public static readonly StyledProperty<IDataTemplate?> RowDetailsDataTemplateProperty = AvaloniaProperty.Register<TreeListView, IDataTemplate?>(nameof(RowDetailsDataTemplate));
    public static readonly StyledProperty<bool> ShowGridProperty = AvaloniaProperty.Register<TreeListView, bool>(nameof(ShowGrid), defaultBindingMode: BindingMode.OneWay, defaultValue: false);
    public static readonly StyledProperty<bool> SelectItemOnRightClickProperty = AvaloniaProperty.Register<TreeListViewItem, bool>(nameof(SelectItemOnRightClick), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<ControlTheme?> ItemContainerThemeProperty = AvaloniaProperty.Register<TreeListViewItem, ControlTheme?>(nameof(ItemContainerTheme));

    public ControlTheme? ItemContainerTheme
    {
        get => GetValue(ItemContainerThemeProperty);
        set => SetValue(ItemContainerThemeProperty, value);
    }

    public bool SelectItemOnRightClick
    {
        get => GetValue(SelectItemOnRightClickProperty);
        set => SetValue(SelectItemOnRightClickProperty, value);
    }

    public string? ChildrenPropertyName
    {
        get => GetValue(ChildrenPropertyNameProperty);
        set => SetValue(ChildrenPropertyNameProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool ShowRowDetails
    {
        get => GetValue(ShowRowDetailsProperty);
        set => SetValue(ShowRowDetailsProperty, value);
    }

    public object? SelectedItemEx
    {
        get => GetValue(SelectedItemExProperty);
        set => SetValue(SelectedItemExProperty, value);
    }

    public GridView? View
    {
        get => GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    [InheritDataTypeFromItems(nameof(ItemsSource))]
    public IDataTemplate? RowDetailsDataTemplate
    {
        get => GetValue(RowDetailsDataTemplateProperty);
        set => SetValue(RowDetailsDataTemplateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ViewProperty)
        {
            UpdateGridView(change.GetNewValue<GridView>());
        }
        else if (change.Property == ItemsSourceProperty)
        {
            UpdateItemsSource(change.GetOldValue<IEnumerable>(), change.GetNewValue<IEnumerable>());
        }
        else if (change.Property == ChildrenPropertyNameProperty)
        {
            UpdateSubPath(change.GetNewValue<string>());
        }
        else if (change.Property == RowDetailsDataTemplateProperty || change.Property == ShowRowDetailsProperty)
        {
            foreach (TreeListViewItem tlv in flattener.GetAll())
            {
                tlv.UpdateRowDetails();
            }
        }
    }

    private readonly StackPanel _rows;
    private readonly Row _header;
    private readonly ObservableCollectionExtended<TreeListViewItem> _rootItems = new();
    private readonly TreeFlattener<TreeListViewItem> flattener;

    public TreeListView()
    {
        _collectionChangedSub = new(this, (target, sender, _, arg) => target.OnCollectionChanged(sender, arg));

        RowDefinitions.Add(new(GridLength.Auto));
        RowDefinitions.Add(new(GridLength.Star));

        _header = new Row();
        _rows = new StackPanel();

        var headerScrollViewer = new ScrollViewer()
        {
            [Grid.RowProperty] = 0,
            Content = _header,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Children.Add(headerScrollViewer);
        ScrollViewer contentScrollViewer = new ScrollViewer() { Content = _rows, [Grid.RowProperty] = 1, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        headerScrollViewer[!ScrollViewer.OffsetProperty] = contentScrollViewer[!ScrollViewer.OffsetProperty];
        Children.Add(contentScrollViewer);

        flattener = new(
            source: new(_rootItems),
            children: item => item.SubRowsReadOnly,
            isHidden: item => item.IsRowHidden,
            isExpanded: item => item.IsExpanded);

        flattener.FlattenedCollection.ToObservableChangeSet()
            .Adapt(new Adaptor(_rows.Children))
            .Subscribe();
    }

    sealed class Adaptor(Controls rowsChildren) : IChangeSetAdaptor<TreeListViewItem>
    {
        public void Adapt(IChangeSet<TreeListViewItem> changes)
        {
            rowsChildren.Clone(changes.Transform(Control (x) => x));
        }
    }

    private readonly TargetWeakEventSubscriber<TreeListView, NotifyCollectionChangedEventArgs> _collectionChangedSub;

    private void UpdateItemsSource(IEnumerable oldEnumerable, IEnumerable newEnumerable)
    {
        if (oldEnumerable is INotifyCollectionChanged oldNtc)
            WeakEvents.CollectionChanged.Unsubscribe(oldNtc, _collectionChangedSub);

        if (newEnumerable is INotifyCollectionChanged newNtc)
            WeakEvents.CollectionChanged.Subscribe(newNtc, _collectionChangedSub);

        if (newEnumerable != null)
        {
            OnCollectionChanged(newEnumerable, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            {
                if (e.NewItems == null || e.NewItems.Count == 0)
                    return;

                var insertIndex = e.NewStartingIndex;
                Debug.Assert(insertIndex >= 0 && insertIndex <= _rootItems.Count);

                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    _rootItems.Insert(insertIndex + i, Create(e.NewItems[i]!));
                }

                break;
            }

            case NotifyCollectionChangedAction.Remove:
            {
                Debug.Assert(e.OldItems != null && e.OldItems.Count != 0);

                var removeIndex = e.OldStartingIndex;

                Debug.Assert(removeIndex >= 0 && removeIndex + e.OldItems.Count <= _rootItems.Count);
                _rootItems.RemoveRange(removeIndex, e.OldItems.Count);

                break;
            }

            case NotifyCollectionChangedAction.Replace:
            {
                if (e.NewItems == null || e.NewItems.Count == 0)
                    return;

                var startIndex = e.NewStartingIndex;

                Debug.Assert(startIndex >= 0 && startIndex + e.NewItems.Count <= _rootItems.Count);
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    _rootItems[startIndex + i] = Create(e.NewItems[i]!);
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
            {
                if (e.OldItems == null || e.OldItems.Count == 0)
                    return;

                var oldIndex = e.OldStartingIndex;
                var newIndex = e.NewStartingIndex;

                Debug.Assert(oldIndex >= 0 && newIndex >= 0 && oldIndex < _rootItems.Count);
                if (e.OldItems.Count == 1)
                {
                    _rootItems.Move(oldIndex, newIndex);
                }
                else
                {
                    TreeListViewItem[] items = new TreeListViewItem[e.OldItems.Count];
                    for (int i = 0; i < e.OldItems.Count; i++)
                    {
                        items[i] = _rootItems[oldIndex];
                        _rootItems.RemoveAt(oldIndex);
                    }

                    var insertIndex = newIndex;
                    if (insertIndex < 0) insertIndex = 0;
                    if (insertIndex > _rootItems.Count) insertIndex = _rootItems.Count;
                    _rootItems.InsertRange(items, insertIndex);
                }

                return;
            }

            case NotifyCollectionChangedAction.Reset:
            default:
            {
                _rootItems.Clear();
                if (sender is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                        _rootItems.Add(Create(item));
                }

                break;
            }
        }

        return;

        TreeListViewItem Create(object item)
        {
            TreeListViewItem row = new(0, this)
            {
                DataContext = item,
                Theme = ItemContainerTheme
            };

            row.UpdateGridView(_header.Children);
            row.UpdateSubPath(ChildrenPropertyName, item);

            row.ApplyStyling();

            return row;
        }
    }

    internal void OnRowIsSelectedChanged(TreeListViewItem treeListViewRow, bool isSelected)
    {
        if (isSelected)
        {
            foreach (TreeListViewItem tlv in flattener.GetAll())
            {
                tlv.IsSelected = treeListViewRow == tlv;
            }

            SelectedItemEx = treeListViewRow.DataContext;
        }
        else if (treeListViewRow.DataContext == SelectedItemEx)
        {
            SelectedItemEx = null;
        }
    }

    public class TreeListViewCell(TreeListViewItem parent, HeaderCell headerColumn, int level) : Cell
    {
        public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<TreeListViewCell, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        public static readonly StyledProperty<bool> IsExpandableProperty = AvaloniaProperty.Register<TreeListViewCell, bool>(nameof(IsExpandable));

        public bool IsExpandable
        {
            get => GetValue(IsExpandableProperty);
            set => SetValue(IsExpandableProperty, value);
        }

        static readonly Geometry OpenGeometry = Geometry.Parse("M 0 0 L 16 0 L 8 8 Z");
        static readonly Geometry ClosedGeometry = Geometry.Parse("M 0 0 L 8 8 L 0 16 Z");

        static TreeListViewCell()
        {
            AffectsRender<TreeListViewCell>([IsExpandedProperty, IsExpandableProperty]);
        }

        bool _isFirst;

        public override void SetIndex(int i)
        {
            Padding = i == 0 ? new(18 + level * 18, 0, 0, 0) : default;
            _isFirst = i == 0;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (!_isFirst || !IsExpandable) return;

            var position = e.GetPosition(this);
            if (position.X > Padding.Left)
            {
                return;
            }

            parent.SetCurrentValue(TreeListViewItem.IsExpandedProperty, !IsExpanded);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsExpandableProperty)
            {
                SetIndex(_isFirst && change.GetNewValue<bool>() ? 0 : 1);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            var headerWidth = headerColumn.DesiredSize.Width;
            if (!double.IsNaN(headerColumn.Width))
            {
                return size.WithWidth(headerWidth);
            }

            if (headerWidth >= size.Width)
            {
                return size.WithWidth(headerWidth);
            }

            headerColumn.ChildColumnWidth = size.Width;
            return size;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (!IsExpandable || !_isFirst)
            {
                return;
            }

            int offset = IsExpanded ? 0 : -4;
            using (context.PushTransform(Matrix.CreateTranslation(2 - offset + level * 18, (Bounds.Height / 2) - (IsExpanded ? 4 : 8))))
            {
                context.DrawGeometry(Brushes.Gray, null, IsExpanded ? OpenGeometry : ClosedGeometry);
            }
        }
    }

    public class Row : Control
    {
        public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<Row, IBrush?>(nameof(Background));

        private double? beforeDetailsRowHeight;

        /// <summary>
        /// Gets or sets a brush with which to paint the background.
        /// </summary>
        public IBrush? Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly StyledProperty<bool> ShowSeparatorsProperty = AvaloniaProperty.Register<Row, bool>(nameof(ShowSeparators), defaultValue: true);

        public bool ShowSeparators
        {
            get => GetValue(ShowSeparatorsProperty);
            set => SetValue(ShowSeparatorsProperty, value);
        }

        public Controls Children { get; } = [];

        static Row()
        {
            AffectsRender<Row>(BackgroundProperty);
            AffectsRender<Row>(ShowSeparatorsProperty);
        }

        public Row()
        {
            Children.CollectionChanged += ChildrenChanged;
        }

        private void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    LogicalChildren.InsertRange(e.NewStartingIndex, e.NewItems!.Cast<Control>());
                    VisualChildren.InsertRange(e.NewStartingIndex, e.NewItems!.Cast<Visual>());
                    break;

                case NotifyCollectionChangedAction.Move:
                    LogicalChildren.MoveRange(e.OldStartingIndex, e.OldItems!.Count, e.NewStartingIndex);
                    VisualChildren.MoveRange(e.OldStartingIndex, e.OldItems!.Count, e.NewStartingIndex);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    LogicalChildren.RemoveAll(e.OldItems!.OfType<Control>().ToList());
                    VisualChildren.RemoveAll(e.OldItems!.OfType<Visual>());
                    break;

                case NotifyCollectionChangedAction.Replace:
                    for (var i = 0; i < e.OldItems!.Count; ++i)
                    {
                        var index = i + e.OldStartingIndex;
                        var child = (Control)e.NewItems![i]!;
                        LogicalChildren[index] = child;
                        VisualChildren[index] = child;
                    }

                    break;

                case NotifyCollectionChangedAction.Reset:
                    throw new NotSupportedException();
            }

            InvalidateMeasure();
        }

        public void Swap(int oldIndex, int newIndex)
        {
            var a = (Cell)Children[oldIndex];
            var b = (Cell)Children[newIndex];

            Children.RemoveAt(oldIndex);
            Children.Insert(newIndex, a);

            Children.RemoveAt(Children.IndexOf(b));
            Children.Insert(oldIndex, b);

            b.SetIndex(oldIndex);
            a.SetIndex(newIndex);
        }

        public sealed override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            var renderSize = bounds.Size;

            var background = Background;
            if (background != null)
            {
                context.FillRectangle(background, new(renderSize));
            }

            if (ShowSeparators)
            {
                if (GetType() == typeof(Row))
                {
                    context.FillRectangle(Brushes.Black, new(0, 0, renderSize.Width, 1));
                }

                context.FillRectangle(Brushes.Black, new(0, renderSize.Height - 1, renderSize.Width, 1));

                if (beforeDetailsRowHeight is double rh)
                {
                    context.FillRectangle(Brushes.Black, new(0, rh, renderSize.Width, 1));
                }
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var constrainedSize = availableSize.WithWidth(double.PositiveInfinity);
            var desiredSize = new Size();
            RowDetailsPresenter? details = null;

            foreach (var child in Children)
            {
                child.Measure(constrainedSize);
                var childSize = child.DesiredSize;

                if (child is RowDetailsPresenter rd)
                {
                    details = rd;
                    continue;
                }

                desiredSize = new(
                    desiredSize.Width + childSize.Width,
                    Math.Max(desiredSize.Height, childSize.Height));
            }

            if (details is { IsVisible: true })
            {
                // Measure details with the row width so it can stretch.
                details.Measure(availableSize.WithWidth(desiredSize.Width));
                var detSize = details.DesiredSize;
                desiredSize = desiredSize.WithHeight(desiredSize.Height + detSize.Height);
            }

            return desiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double prevChild = 0;
            double rowHeight = 0;
            RowDetailsPresenter? details = null;

            foreach (var child in Children)
            {
                if (child is RowDetailsPresenter rd)
                {
                    details = rd;
                    continue;
                }

                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            foreach (var child in Children)
            {
                if (child is RowDetailsPresenter) continue;

                var width = child.DesiredSize.Width;
                child.Arrange(new Rect(prevChild, 0, width, rowHeight));
                prevChild += width;
            }

            if (details is { IsVisible: true })
            {
                var rowWidth = Math.Max(prevChild, finalSize.Width);
                var detailsHeight = details.DesiredSize.Height;
                beforeDetailsRowHeight = rowHeight;
                details.Arrange(new Rect(0, rowHeight, rowWidth, detailsHeight));
            }
            else
            {
                beforeDetailsRowHeight = null;
            }

            return finalSize;
        }
    }

    public class RowDetailsPresenter : ContentControl
    {
    }

    public abstract class Cell : ContentControl
    {
        public static readonly StyledProperty<bool> ShowBordersProperty = AvaloniaProperty.Register<Cell, bool>(nameof(ShowBorders), defaultValue: true);

        public bool ShowBorders
        {
            get => GetValue(ShowBordersProperty);
            set => SetValue(ShowBordersProperty, value);
        }

        static Cell()
        {
            AffectsRender<Cell>(ShowBordersProperty);
        }

        public virtual void SetIndex(int i)
        {
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (ShowBorders)
            {
                var renderSize = Bounds.Size;
                context.FillRectangle(Brushes.Black, new(renderSize.Width - 1, 0, 1, renderSize.Height));
            }
        }
    }

    public class HeaderCell(TreeListView parent, GridViewColumn column) : Cell
    {
        private double childColumnWidth;

        internal double ChildColumnWidth
        {
            get => childColumnWidth;
            set
            {
                childColumnWidth = value;
                InvalidateMeasure();
            }
        }

        public GridViewColumn Column { get; } = column;

        protected override Size MeasureCore(Size availableSize)
        {
            Size size = base.MeasureCore(availableSize);

            if (double.IsNaN(Width) && ChildColumnWidth > size.Width)
            {
                size = size.WithWidth(ChildColumnWidth);
            }

            foreach (var c in parent._rows.Children.Cast<Row>().SelectMany(x => x.Children).OfType<Cell>()) c.InvalidateMeasure();

            return size;
        }
    }

    void UpdateGridView(GridView cols)
    {
        _header.Children.Clear();

        for (int i = 0; i < cols.Columns.Count; i++)
        {
            GridViewColumn col = cols.Columns[i];
            _header.Children.Add(
                new HeaderCell(this, col)
                {
                    MinWidth = 25,
                    [!HeaderCell.ContentProperty] = col[!GridViewColumn.HeaderProperty],
                    [!HeaderCell.WidthProperty] = col[!!GridViewColumn.WidthProperty]
                });
        }

        foreach (TreeListViewItem tlv in flattener.GetAll()) tlv.UpdateGridView(_header.Children);

        InvalidateMeasure();
    }

    private void UpdateSubPath(string childrenPropName)
    {
        foreach (TreeListViewItem tlv in flattener.GetAll()) tlv.UpdateSubPath(childrenPropName, tlv.DataContext);
    }

    #region Columns resize and reorder

    const int TLV_HeaderResizeMargin = 5;

    private static readonly Cursor _resizeCursor = new(StandardCursorType.SizeWestEast);
    private Cursor? _oldCursor;
    private HeaderCell? _resizingCell;

    private Control? _draggableControl;

    private HeaderCell? GetHeaderCell(Point pos)
    {
        HeaderCell? hitChild = null;
        foreach (var child in _header.Children)
        {
            if (child.Bounds.Contains(pos))
            {
                hitChild = child as HeaderCell;
                break;
            }
        }

        return hitChild;
    }

    private HeaderCell? GetResizeCell(PointerEventArgs e)
    {
        if (GetHeaderCell(e.GetPosition(_header)) is not { } hitChild)
        {
            return null;
        }

        var pos = e.GetPosition(hitChild);

        if (pos.X < TLV_HeaderResizeMargin)
        {
            var idx = _header.Children.IndexOf(hitChild);
            return idx > 0 ? (HeaderCell)_header.Children[idx - 1] : null;
        }
        else if (hitChild.Bounds.Width - pos.X < TLV_HeaderResizeMargin)
        {
            return hitChild;
        }
        else
        {
            return null;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_draggableControl != null)
        {
            StopDrag();
        }

        if (Cursor == _resizeCursor)
        {
            if (GetResizeCell(e) is { } cell)
            {
                _resizingCell = cell;
            }
            else
            {
                _resizingCell = null;
            }

            return;
        }

        var hitChild = GetHeaderCell(e.GetPosition(_header));
        if (hitChild == null)
        {
            return;
        }

        object? content = hitChild.Content;
        if (content is Control)
        {
            _draggableControl = new Rectangle()
            {
                Width = _header.DesiredSize.Width,
                Height = _header.DesiredSize.Height,
                Fill = new VisualBrush
                {
                    Visual = hitChild,
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                }
            };
        }
        else
        {
            _draggableControl = new ContentControl() { Content = content };
        }

        _draggableControl[Canvas.LeftProperty] = hitChild.Bounds.Left;
        _draggableControl.IsEnabled = false;
        _draggableControl.IsHitTestVisible = false;
        _draggableControl.Opacity = 0.7;
        _draggableControl.Tag = hitChild;

        AdornerLayer.SetAdorner(_header, new Canvas() { Children = { _draggableControl } });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggableControl != null)
        {
            _draggableControl[Canvas.LeftProperty] = e.GetPosition(_header).X;
            return;
        }

        if (_resizingCell != null)
        {
            var width = _resizingCell.Bounds.Width - e.GetPosition(_resizingCell).X;
            var res = _resizingCell.Bounds.Width - width;
            if (res > 0 && res > _resizingCell.MinWidth)
            {
                _resizingCell.Width = res;
            }
        }
        else if (GetResizeCell(e) is not null)
        {
            if (Cursor != _resizeCursor)
            {
                _oldCursor = Cursor;
                Cursor = _resizeCursor;
            }
        }
        else if (Cursor == _resizeCursor)
        {
            Cursor = _oldCursor;
        }
    }

    private void StopDrag()
    {
        AdornerLayer.SetAdorner(_header, null);
        _draggableControl = null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_resizingCell != null)
        {
            _resizingCell = null;
            Cursor = _oldCursor;
            return;
        }

        if (_draggableControl == null)
        {
            return;
        }

        HeaderCell? dragCell = _draggableControl.Tag as HeaderCell;
        StopDrag();
        if (dragCell == null)
        {
            return;
        }

        var cell = GetHeaderCell(e.GetPosition(_header).WithY(0));
        if (cell == null || dragCell == cell)
        {
            return;
        }

        int oldIndex = _header.Children.IndexOf(dragCell);
        int newIndex = _header.Children.IndexOf(cell);
        _header.Swap(oldIndex, newIndex);
        foreach (TreeListViewItem row in flattener.GetAll())
        {
            row.Swap(oldIndex, newIndex);
        }
    }

    #endregion
}

public class TreeListViewItem : TreeListView.Row
{
    public static readonly StyledProperty<bool> IsRowHiddenProperty = AvaloniaProperty.Register<TreeListViewItem, bool>(nameof(IsRowHidden), defaultBindingMode: BindingMode.TwoWay);

    public bool IsRowHidden
    {
        get => GetValue(IsRowHiddenProperty);
        set => SetValue(IsRowHiddenProperty, value);
    }

    public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<TreeListViewItem, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<TreeListViewItem, bool>(nameof(IsSelected), defaultBindingMode: BindingMode.TwoWay);

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly StyledProperty<bool> HasChildrenProperty = AvaloniaProperty.Register<TreeListViewItem, bool>(nameof(HasChildren), defaultBindingMode: BindingMode.TwoWay);

    public bool HasChildren
    {
        get => GetValue(HasChildrenProperty);
        set => SetValue(HasChildrenProperty, value);
    }

    private IList<Control>? _currentColumns;

    string? _childPropertyName;

    public TreeListViewItem(int level, TreeListView parent)
    {
        _propertyChangedSub = new(this, (target, sender, _, arg) => target.ItemPropertyChanged(sender, arg));
        _collectionChangedSub = new(this, (target, sender, _, arg) => target.ItemCollectionChanged(sender, arg));

        Level = level;
        _parentTree = parent;
        this[!ShowSeparatorsProperty] = parent[!TreeListView.ShowGridProperty];
        SetValue(BackgroundProperty, Brushes.Transparent, BindingPriority.Style);

        SubRowsReadOnly = new(SubRows);
    }

    private TreeListView.RowDetailsPresenter? _rowDetailsPresenter;

    public int Level { get; }
    private readonly TreeListView _parentTree;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsRowHiddenProperty)
        {
        }

        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            string? childPropertyName = _childPropertyName;
            UpdateSubPath(null, change.OldValue);
            UpdateSubPath(childPropertyName, change.NewValue);
            UpdateRowDetails();
        }
        else if (change.Property == IsSelectedProperty)
        {
            _parentTree.OnRowIsSelectedChanged(this, IsSelected);
            UpdateRowDetails();
        }

        if (change.Property == IsPointerOverProperty || change.Property == IsSelectedProperty)
        {
            UpdateBackground(change.GetNewValue<bool>());
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        IsSelected = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.ClickCount == 2 && Children.Count > 0)
        {
            if (e.GetPosition(this).X <= ((TreeListView.TreeListViewCell)Children[0]).Padding.Left)
            {
                return;
            }

            SetCurrentValue(IsExpandedProperty, !IsExpanded);
        }
    }

    private void UpdateBackground(bool isPointerOver)
    {
        IBrush? brush;
        if (isPointerOver || IsSelected)
        {
            brush = this.TryFindResource("TreeListViewSelectedBrush", ActualThemeVariant, out var obj)
                ? obj as IBrush
                : this.TryFindResource("SystemControlHighlightListAccentLowBrush", ActualThemeVariant, out obj)
                    ? obj as IBrush
                    : Brushes.Gray;
        }
        else
        {
            brush = Brushes.Transparent;
        }

        SetValue(BackgroundProperty, brush, BindingPriority.Style);
    }

    private readonly TargetWeakEventSubscriber<TreeListViewItem, PropertyChangedEventArgs> _propertyChangedSub;
    private readonly TargetWeakEventSubscriber<TreeListViewItem, NotifyCollectionChangedEventArgs> _collectionChangedSub;

    internal void UpdateSubPath(string? name, object? dataContext)
    {
        _childPropertyName = name;

        if (dataContext is INotifyPropertyChanged npc)
        {
            WeakEvents.ThreadSafePropertyChanged.Unsubscribe(npc, _propertyChangedSub);
            if (name != null)
            {
                WeakEvents.ThreadSafePropertyChanged.Subscribe(npc, _propertyChangedSub);
            }
        }

        ItemPropertyChanged(dataContext, new PropertyChangedEventArgs(name));
    }

    object? _currentCollection;

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == null || _childPropertyName == null || e.PropertyName == null)
        {
            ClearCurrentCollection();
            return;
        }

        if (e.PropertyName != _childPropertyName) return;

        ClearCurrentCollection();

        var prop = sender.GetType().GetProperty(_childPropertyName);
        if (prop == null)
        {
            return;
        }

        var newCollection = prop.GetValue(sender);
        _currentCollection = newCollection;
        if (newCollection is not IList initialItems)
        {
            return;
        }

        if (initialItems is INotifyCollectionChanged newNotify)
        {
            WeakEvents.CollectionChanged.Subscribe(newNotify, _collectionChangedSub);
        }

        ItemCollectionChanged(initialItems, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return;

        void ClearCurrentCollection()
        {
            if (_currentCollection is INotifyCollectionChanged currentNotify)
            {
                WeakEvents.CollectionChanged.Unsubscribe(currentNotify, _collectionChangedSub);
            }

            ItemCollectionChanged(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            _currentCollection = null;
        }
    }

    internal readonly ObservableCollectionExtended<TreeListViewItem> SubRows = new();
    internal readonly ReadOnlyObservableCollection<TreeListViewItem> SubRowsReadOnly;

    private void ItemCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
            {
                if (e.NewItems == null || e.NewItems.Count == 0)
                    break;

                var insertIndex = e.NewStartingIndex;
                if (insertIndex < 0 || insertIndex > SubRows.Count)
                {
                    insertIndex = SubRows.Count;
                }

                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    SubRows.Insert(insertIndex + i, Create(e.NewItems[i]!));
                }

                break;
            }

            case NotifyCollectionChangedAction.Remove:
            {
                Debug.Assert(e.OldItems != null && e.OldItems.Count != 0);

                var removeIndex = e.OldStartingIndex;

                Debug.Assert(removeIndex >= 0 && removeIndex + e.OldItems.Count <= SubRows.Count);
                SubRows.RemoveRange(removeIndex, e.OldItems.Count);

                break;
            }

            case NotifyCollectionChangedAction.Replace:
            {
                if (e.NewItems == null || e.NewItems.Count == 0)
                    break;

                var startIndex = e.NewStartingIndex;

                Debug.Assert(startIndex >= 0 && startIndex + e.NewItems.Count <= SubRows.Count);
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    SubRows[startIndex + i] = Create(e.NewItems[i]!);
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
            {
                if (e.OldItems == null || e.OldItems.Count == 0)
                    break;

                var oldIndex = e.OldStartingIndex;
                var newIndex = e.NewStartingIndex;

                Debug.Assert(oldIndex >= 0 && newIndex >= 0 && oldIndex < SubRows.Count);
                if (e.OldItems.Count == 1)
                {
                    SubRows.Move(oldIndex, newIndex);
                }
                else
                {
                    TreeListViewItem[] items = new TreeListViewItem[e.OldItems.Count];
                    for (int i = 0; i < e.OldItems.Count; i++)
                    {
                        items[i] = SubRows[oldIndex];
                        SubRows.RemoveAt(oldIndex);
                    }

                    var insertIndex = newIndex;
                    if (insertIndex < 0) insertIndex = 0;
                    if (insertIndex > SubRows.Count) insertIndex = SubRows.Count;
                    SubRows.InsertRange(items, insertIndex);
                }

                break;
            }

            case NotifyCollectionChangedAction.Reset:
            default:
            {
                SubRows.Clear();
                if (sender is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                        SubRows.Add(Create(item));
                }

                break;
            }
        }

        bool old = HasChildren;
        HasChildren = SubRows.Count > 0;
        if (old != HasChildren)
        {
            InvalidateVisual();
        }

        return;

        TreeListViewItem Create(object item)
        {
            TreeListViewItem row = new(Level + 1, _parentTree)
            {
                DataContext = item,
                Theme = Theme
            };

            if (_currentColumns != null) row.UpdateGridView(_currentColumns);
            row.UpdateSubPath(_childPropertyName, item);

            row.ApplyStyling();

            return row;
        }
    }

    internal void UpdateGridView(IList<Control> columns)
    {
        if (_currentColumns == columns)
        {
            return;
        }

        _currentColumns = columns;
        Children.Clear();
        _rowDetailsPresenter = null;

        for (int i = 0; i < columns.Count; i++)
        {
            TreeListView.HeaderCell headerCell = (TreeListView.HeaderCell)columns[i];
            GridViewColumn col = headerCell.Column;
            var cell = new TreeListView.TreeListViewCell(this, headerCell, Level)
            {
                Content = col?.CellTemplate?.Build(DataContext),
                [!TreeListView.TreeListViewCell.IsExpandedProperty] = this[!IsExpandedProperty],
                [!TreeListView.TreeListViewCell.IsExpandableProperty] = this[!HasChildrenProperty],
                [!TreeListView.Cell.ShowBordersProperty] = this[!ShowSeparatorsProperty],
            };
            cell.SetIndex(i);

            Children.Add(cell);
        }

        UpdateRowDetails();
    }

    internal void UpdateRowDetails()
    {
        var template = _parentTree.RowDetailsDataTemplate;

        if (template == null || (!IsSelected && !_parentTree.ShowRowDetails))
        {
            if (_rowDetailsPresenter != null)
            {
                Children.Remove(_rowDetailsPresenter);
                _rowDetailsPresenter = null;
            }

            return;
        }

        var content = template.Build(DataContext);
        if (content == null)
        {
            if (_rowDetailsPresenter != null)
            {
                Children.Remove(_rowDetailsPresenter);
                _rowDetailsPresenter = null;
            }

            return;
        }

        if (_rowDetailsPresenter == null)
        {
            _rowDetailsPresenter = new TreeListView.RowDetailsPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(5 + Level * 15, 2, 0, 2)
            };

            Children.Add(_rowDetailsPresenter);
        }

        _rowDetailsPresenter.Content = content;
    }
}

public class GridView : AvaloniaObject
{
    private readonly ObservableCollection<GridViewColumn> _columns = [];
    [Content] public ObservableCollection<GridViewColumn> Columns => _columns;
}

public class GridViewColumn : AvaloniaObject
{
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<GridViewColumn, object?>(nameof(Header));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly StyledProperty<double> WidthProperty = AvaloniaProperty.Register<GridViewColumn, double>(nameof(Width), defaultValue: double.NaN);

    public double Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> CellTemplateProperty = AvaloniaProperty.Register<GridViewColumn, IDataTemplate?>(nameof(CellTemplate));

    [InheritDataTypeFromItems(nameof(TreeListView.ItemsSource), AncestorType = typeof(TreeListView))]
    public IDataTemplate? CellTemplate
    {
        get => GetValue(CellTemplateProperty);
        set => SetValue(CellTemplateProperty, value);
    }
}