using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Client.Views.Common;

/// <summary>Reserves the desktop scrollbar strip while the bar itself remains automatic.</summary>
public class StableScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar")?.Parent is Grid layout &&
            layout.ColumnDefinitions.Count > 1)
            layout.ColumnDefinitions[1].Width = new GridLength(16);
    }
}
