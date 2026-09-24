using System;
using Avalonia.Controls;
using GsCan.View.Session;

namespace GsCan.View
{
    public partial class MainWindow : Window
    {
        public MainWindow()
            : this(new ViewSession(new NullGsCanPort()))
        {
        }

        public MainWindow(ViewSession session)
        {
            InitializeComponent();
            DataContext = session ?? throw new ArgumentNullException(nameof(session));
        }
    }
}
