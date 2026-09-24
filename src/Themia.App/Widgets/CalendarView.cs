using Microsoft.UI.Xaml.Controls;
using Themia.App.Services;
using Themia.Core.Model;

namespace Themia.App.Widgets;

/// <summary>Placeholder: the real Calendar widget replaces this file wholesale.</summary>
internal sealed partial class CalendarView : Grid, IWidgetView
{
    public void Attach(Ticker ticker, WidgetConfig config) => ApplyConfig(config);

    public void ApplyConfig(WidgetConfig config)
    {
    }

    public void Detach()
    {
    }
}
