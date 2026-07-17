using Avalonia.Controls;
using Avalonia.Threading;
using Maki.Models;
using Maki.ViewModels;
using System.Collections.Specialized;

namespace Maki.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // Auto-scroll al llegar nuevos mensajes
        vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Suscribirse también a cambios de contenido (streaming token a token)
        if (e.NewItems is not null)
        {
            foreach (ChatMessage msg in e.NewItems)
            {
                msg.PropertyChanged += (_, _) =>
                    Dispatcher.UIThread.Post(ScrollToBottom);
            }
        }

        Dispatcher.UIThread.Post(ScrollToBottom);
    }

    private void ScrollToBottom()
    {
        if (this.FindControl<ScrollViewer>("ChatScroll") is { } sv)
            sv.ScrollToEnd();
    }
}
