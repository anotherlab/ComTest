using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ComTest;

public sealed partial class BufferPanel : UserControl
{
    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<BufferPanel, IBrush?>(nameof(Accent));
    public IBrush? Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public event EventHandler? ClearRequested;
    public BufferPanel() => InitializeComponent();
    private void ClearClicked(object? sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);
    public void FollowLatest() => DataScroll.ScrollToEnd();
}
