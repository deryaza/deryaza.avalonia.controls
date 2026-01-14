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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DynamicData;
using DynamicData.Aggregation;
using DynamicData.Binding;
using ReactiveUI;

namespace deryaza.avalonia.controls;

public class TimelineItem : ReactiveObject
{
    private bool isExpanded = true;

    public string Name { get; set; } = string.Empty;

    public DateTime Start { get; set; }

    public DateTime? End { get; set; }

    public bool IsExpanded { get => isExpanded; set => this.RaiseAndSetIfChanged(ref isExpanded, value); }

    public bool IsHidden { get; set; } = false;

    public int Level { get; set; }

    public IBrush? Brush { get; set; }

    public ObservableCollection<TimelineItem> Children { get; } = new();
}


[TemplatePart("PART_Grid", typeof(Grid))]
[TemplatePart("PART_Canvas", typeof(Canvas))]
[TemplatePart("PART_ScrollBar", typeof(ScrollBar))]
[TemplatePart("PART_VScrollBar", typeof(ScrollBar))]
public class TimelineView : TemplatedControl
{
    public static readonly StyledProperty<ObservableCollection<TimelineItem>?> ItemsProperty =
        AvaloniaProperty.Register<TimelineView, ObservableCollection<TimelineItem>?>(nameof(Items));

    public ObservableCollection<TimelineItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public static readonly StyledProperty<double> SnapPointsDistanceProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(SnapPointsDistance), defaultValue: 160);
    public double SnapPointsDistance
    {
        get => GetValue(SnapPointsDistanceProperty);
        set => SetValue(SnapPointsDistanceProperty, value);
    }

    public static readonly StyledProperty<TimeSpan> SnapPointStepProperty = AvaloniaProperty.Register<TimelineView, TimeSpan>(nameof(SnapPointStep), defaultValue: TimeSpan.FromMinutes(1));
    public TimeSpan SnapPointStep
    {
        get => GetValue(SnapPointStepProperty);
        set => SetValue(SnapPointStepProperty, value);
    }

    public static readonly StyledProperty<double> SnapPointsHeightProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(SnapPointsHeight), defaultValue: 30);
    public double SnapPointsHeight
    {
        get => GetValue(SnapPointsHeightProperty);
        set => SetValue(SnapPointsHeightProperty, value);
    }


    public static readonly StyledProperty<double> ItemHeigthProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(ItemHeigth), defaultValue: 22);
    public double ItemHeigth
    {
        get => GetValue(ItemHeigthProperty);
        set => SetValue(ItemHeigthProperty, value);
    }

    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(ItemSpacing), defaultValue: 4);
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    public static readonly StyledProperty<double> LevelIndentProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(LevelIndent), defaultValue: 10);
    public double LevelIndent
    {
        get => GetValue(LevelIndentProperty);
        set => SetValue(LevelIndentProperty, value);
    }

    /*
    public static readonly StyledProperty<double> ScrollValueProperty = AvaloniaProperty.Register<TimelineView, double>(nameof(ScrollValue));
    public double ScrollValue
    {
        get => GetValue(ScrollValueProperty);
        set => SetValue(ScrollValueProperty, value);
    }
    */

    public static readonly StyledProperty<DateTime> ScrollValueProperty = AvaloniaProperty.Register<TimelineView, DateTime>(nameof(ScrollValue));
    public DateTime ScrollValue
    {
        get => GetValue(ScrollValueProperty);
        set => SetValue(ScrollValueProperty, value);
    }

    public static readonly StyledProperty<IBrush> DefaultItemBrushProperty = AvaloniaProperty.Register<TimelineView, IBrush>(nameof(DefaultItemBrush), defaultValue: Brushes.MediumVioletRed);
    public IBrush DefaultItemBrush
    {
        get => GetValue(DefaultItemBrushProperty);
        set => SetValue(DefaultItemBrushProperty, value);
    }

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<TimelineView, double>(nameof(VerticalOffset));

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    DateTime from;
    DateTime to;
    readonly ReadOnlyObservableCollection<FlatTimelineRow> flattened;

    Grid g;
    Canvas c;
    ScrollBar sb;
    ScrollBar vsb;
    bool _syncingScroll;

    static TimelineView()
    {
        TemplateProperty.OverrideDefaultValue<TimelineView>(new FuncControlTemplate(static (_, ns) =>
        {
            var g = new Grid { Name = "PART_Grid" };
            g.RowDefinitions.Add(new(GridLength.Star)); // canvas row
            g.RowDefinitions.Add(new(GridLength.Auto)); // horizontal scrollbar row
            g.ColumnDefinitions.Add(new(GridLength.Star)); // canvas col
            g.ColumnDefinitions.Add(new(GridLength.Auto)); // vertical scrollbar col

            var c = new Canvas
            {
                [Grid.RowProperty] = 0,
                [Grid.ColumnProperty] = 0,
                Name = "PART_Canvas",
                ClipToBounds = true,
                Background = Brushes.Transparent
            };

            var hsb = new ScrollBar
            {
                [Grid.RowProperty] = 1,
                [Grid.ColumnProperty] = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Name = "PART_ScrollBar"
            };

            var vsb = new ScrollBar
            {
                [Grid.RowProperty] = 0,
                [Grid.ColumnProperty] = 1,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                Orientation = Avalonia.Layout.Orientation.Vertical,
                Name = "PART_VScrollBar"
            };

            // bottom-right corner filler
            var corner = new Border
            {
                [Grid.RowProperty] = 1,
                [Grid.ColumnProperty] = 1
            };

            g.Children.AddRange([c, hsb, vsb, corner]);

            g.RegisterInNameScope(ns);
            c.RegisterInNameScope(ns);
            hsb.RegisterInNameScope(ns);
            vsb.RegisterInNameScope(ns);

            return g;
        }));
    }

    public sealed record FlatTimelineRow(TimelineItem Item, int Depth);

    public TimelineView()
    {
        IObservable<IChangeSet<TimelineItem>> common = this.WhenAnyValue(x => x.Items).Select(x => x?.ToObservableChangeSet() ?? Observable.Return(ChangeSet<TimelineItem>.Empty))
            .Switch()
            .TransformMany(x => (IEnumerable<TimelineItem>)[x, .. x.Children])
            .Publish()
            .RefCount();

        IObservable<IChangeSet<TimelineItem>> a = this.WhenAnyValue(x => x.Items).Select(x => x?.ToObservableChangeSet() ?? Observable.Return(ChangeSet<TimelineItem>.Empty))
            .Switch();

        this.WhenAnyValue(x => x.Items).Select(x => x?.ToObservableChangeSet() ?? Observable.Return(ChangeSet<TimelineItem>.Empty))
            .Switch()
            .Bind(out var items)
            .Subscribe();

        var f = new TreeFlattener<TimelineItem>(
                    source: items,
                    children: x => new(x.Children),
                    isHidden: x => x.IsHidden,
                    isExpanded: x => x.IsExpanded
                );

        f.FlattenedCollection
            .ToObservableChangeSet()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Transform(x => new FlatTimelineRow(x, x.Level))
            .Bind(out flattened)
            .Subscribe();

        /*
        IObservable<IChangeSet<FlatTimelineRow>> Do(TimelineItem item, int level)
        {
            var changeSet = new ChangeSet<FlatTimelineRow>();
            changeSet.Add(new(ListChangeReason.Add, new FlatTimelineRow(item, level)));
            IObservable<IChangeSet<FlatTimelineRow>> set = Observable.Return<IChangeSet<FlatTimelineRow>>(changeSet);

            IObservable<IChangeSet<FlatTimelineRow>> others =
                item.WhenAnyValue(x => x.IsExpanded).Select(x => x
                        ? item.Children.ToObservableChangeSet().MergeManyChangeSets(x => Do(x, level + 1))
                        : Observable.Return(ChangeSet<FlatTimelineRow>.Empty))
                .Switch();
            return set.Or(others: others);
        }
        a.MergeManyChangeSets(x => Do(x, 0))
            .Bind(out flattened)
            .Subscribe();
            */


        common.AutoRefresh(x => x.Start).Transform(x => x.Start, true).Minimum(x => x).Subscribe(x => from = x);
        common.AutoRefresh(x => x.End).Transform(x => x.End, true)!.Maximum(x => x ?? default).Subscribe(x => to = x);
    }
    private static readonly TimeSpan[] SnapStepPresets =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
    };

    private int FindNearestPresetIndex(TimeSpan value)
    {
        if (SnapStepPresets.Length == 0) return 0;

        var bestIdx = 0;
        var bestDelta = Math.Abs((SnapStepPresets[0] - value).Ticks);

        for (int i = 1; i < SnapStepPresets.Length; i++)
        {
            var delta = Math.Abs((SnapStepPresets[i] - value).Ticks);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    private void ChangeSnapStep(int direction)
    {
        var idx = FindNearestPresetIndex(SnapPointStep);
        idx = Math.Clamp(idx + direction, 0, SnapStepPresets.Length - 1);
        SnapPointStep = SnapStepPresets[idx];
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        g = e.NameScope.Find<Grid>("PART_Grid");
        c = e.NameScope.Find<Canvas>("PART_Canvas");
        sb = e.NameScope.Find<ScrollBar>("PART_ScrollBar");
        vsb = e.NameScope.Find<ScrollBar>("PART_VScrollBar");

        vsb.GetObservable(RangeBase.ValueProperty).Subscribe(v =>
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            try { VerticalOffset = v; }
            finally { _syncingScroll = false; }
        });

        this.GetObservable(VerticalOffsetProperty).Subscribe(v =>
        {
            if (_syncingScroll) return;
            _syncingScroll = true;
            try { vsb.Value = Math.Clamp(v, vsb.Minimum, vsb.Maximum); }
            finally { _syncingScroll = false; }
        });

        c.PointerWheelChanged += (_, ev) =>
        {
            if (ev.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var dir = ev.Delta.Y > 0 ? -1 : +1;
                ChangeSnapStep(dir);
                ev.Handled = true;
                return;
            }

            if (ev.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                sb.Value = Math.Clamp(sb.Value - ev.Delta.Y * sb.SmallChange, sb.Minimum, sb.Maximum);
            }
            else
            {
                vsb.Value = Math.Clamp(vsb.Value - ev.Delta.Y * vsb.SmallChange, vsb.Minimum, vsb.Maximum);
            }

            ev.Handled = true;
        };

        this.WhenAnyValue(x => x.Bounds)
            .Merge(flattened.ToObservableChangeSet().ObserveOn(RxApp.MainThreadScheduler).Select(_ => Bounds))
            .Merge(sb.GetObservable(RangeBase.ValueProperty).Select(_ => Bounds))
            .Merge(vsb.GetObservable(RangeBase.ValueProperty).Select(_ => Bounds))
            .Merge(this.GetObservable(ScrollValueProperty).Select(_ => Bounds))
            .Merge(this.GetObservable(VerticalOffsetProperty).Select(_ => Bounds))
            .Merge(this.GetObservable(SnapPointStepProperty).Select(_ => Bounds))
            .Subscribe(UpdateTree);
    }

    private void UpdateTree(Rect x)
    {
        if (c is null || sb is null) return;
        if (flattened is null || flattened.Count == 0)
        {
            c.Children.Clear();
            sb.Minimum = 0;
            sb.Maximum = 0;
            sb.ViewportSize = 0;
            sb.SmallChange = SnapPointStep.TotalMilliseconds;
            return;
        }

        c.Children.Clear();

        var fontSize = FontSize;

        double rowsStart = FontSize + 2;
        a(0, rowsStart, new Line { EndPoint = new(x.Width, 0), StrokeThickness = 0.5, Stroke = Brushes.Black });
        rowsStart += ItemSpacing;

        double headersLength = 0;
        {
            int idx = 0;
            for (double y = rowsStart; y < x.Height && idx < flattened.Count; y += ItemHeigth + ItemSpacing, idx++)
            {
                (TimelineItem item, int level) = flattened[idx];
                double itemIndent = level * LevelIndent;

                var tb = new TextBlock { Text = item.Name, FontSize = fontSize };
                tb.Measure(Size.Infinity);

                headersLength = Math.Max(headersLength, tb.DesiredSize.Width + itemIndent);
            }
        }

        double colStart = headersLength + ItemSpacing;

        var pointsPerTime = SnapPointsDistance / SnapPointStep.TotalMilliseconds;

        var totalMs = Math.Max(0, (to - from).TotalMilliseconds);
        var visibleTimelineWidth = Math.Max(0, x.Width - colStart);
        var visibleMs = visibleTimelineWidth <= 0 ? 0 : (visibleTimelineWidth / pointsPerTime);
        var maxMs = Math.Max(0, totalMs - visibleMs);

        _syncingScroll = true;
        try
        {
            sb.Minimum = 0;
            sb.Maximum = maxMs;
            sb.ViewportSize = visibleMs;
            sb.SmallChange = Math.Max(1, SnapPointStep.TotalMilliseconds);
            sb.LargeChange = Math.Max(sb.SmallChange, visibleMs * 0.8);

            var desiredOffsetMs =
                ScrollValue == default ? sb.Value : Math.Max(0, (ScrollValue - from).TotalMilliseconds);

            sb.Value = Math.Clamp(desiredOffsetMs, 0, maxMs);
        }
        finally { _syncingScroll = false; }

        var offsetMs = sb.Value;
        var offsetPx = offsetMs * pointsPerTime;

        var visibleFrom = from + TimeSpan.FromMilliseconds(offsetMs);

        double headerHeight = SnapPointsHeight;
        double rowTop = headerHeight + ItemSpacing;

        double rowStep = ItemHeigth + ItemSpacing;

        double contentHeight = rowTop + flattened.Count * rowStep;
        double maxV = Math.Max(0, contentHeight - x.Height);

        _syncingScroll = true;
        try
        {
            vsb.Minimum = 0;
            vsb.Maximum = maxV;
            vsb.ViewportSize = Math.Max(0, x.Height);
            vsb.SmallChange = rowStep;
            vsb.LargeChange = Math.Max(rowStep, x.Height * 0.8);

            vsb.Value = Math.Clamp(VerticalOffset, 0, maxV);
        }
        finally { _syncingScroll = false; }

        var vOffset = vsb.Value;

        int drawIdx = 0;
        for (double y = rowsStart - vOffset; y < x.Height && drawIdx < flattened.Count; y += ItemHeigth + ItemSpacing, drawIdx++)
        {
            if (y + ItemHeigth < headerHeight) continue;
            if (y > x.Height) break;

            (TimelineItem item, _) = flattened[drawIdx];

            var startPx = colStart + (pointsPerTime * (item.Start - from).TotalMilliseconds) - offsetPx;

            var endTime = item.End ?? (visibleFrom + TimeSpan.FromMilliseconds(visibleMs));
            var widthPx = pointsPerTime * Math.Max(0, (endTime - item.Start).TotalMilliseconds);

            a(startPx, y,
                new Rectangle
                {
                    Height = ItemHeigth - ItemSpacing,
                    Width = widthPx,
                    Fill = item.Brush ?? DefaultItemBrush
                });
        }

        drawIdx = 0;
        for (double y = rowsStart - vOffset; y < x.Height && drawIdx < flattened.Count; y += ItemHeigth + ItemSpacing, drawIdx++)
        {
            if (y + ItemHeigth < headerHeight) continue;
            if (y > x.Height) break;

            (TimelineItem item, int level) = flattened[drawIdx];
            double itemIndent = level * LevelIndent;

            a(0, y + ItemHeigth, new Line { EndPoint = new(x.Width, 0), StrokeThickness = 0.5, Stroke = Brushes.Black });

            var tb = new TextBlock { Text = item.Name, FontSize = fontSize };
            tb.PointerPressed += (_, _) =>
            {
                item.IsExpanded = !item.IsExpanded;
            };
            a(itemIndent, y, tb);
        }

        var stepMs = SnapPointStep.TotalMilliseconds;
        if (stepMs <= 0) stepMs = 1;

        var alignedOffsetMs = offsetMs - (offsetMs % stepMs);
        var currentSnap = from + TimeSpan.FromMilliseconds(alignedOffsetMs);

        var firstX = colStart - (offsetPx % SnapPointsDistance);

        for (double headerStart = firstX; headerStart < x.Width; headerStart += SnapPointsDistance)
        {
            a(headerStart, 0, new TextBlock
            {
                Text = currentSnap.ToString(),
                FontSize = fontSize
            });

            a(headerStart, 0, new Line
            {
                EndPoint = new(0, x.Height),
                StrokeThickness = 0.5,
                Opacity = 1,
                Stroke = Brushes.Black
            });

            currentSnap += SnapPointStep;
        }

        void a(double xx, double yy, Control cc)
        {
            cc[Canvas.LeftProperty] = xx;
            cc[Canvas.TopProperty] = yy;
            c.Children.Add(cc);
        }
    }
}
