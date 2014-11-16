﻿using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace RTF.Applications
{
    /// <summary>
    /// Interaction logic for View.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly RunnerViewModel vm;

        public MainWindow()
        {
            InitializeComponent();

            vm = new RunnerViewModel(new WpfContext());

            DataContext = vm;

            Closing += View_Closing;
        }

        private void View_Closing(object sender, CancelEventArgs e)
        {
            vm.CleanupCommand.Execute();
        }

        private void TestDataTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            vm.SelectedItem = e.NewValue;
        }

        private void UpdateRequired(object sender, RoutedEventArgs e)
        {
            vm.UpdateCommand.Execute();
        }

        private void ProductSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            vm.ChangeProductCommand.Execute(((ComboBox) sender).SelectedIndex);
        }
    }
}
