﻿using System.ComponentModel;
using System.Windows;

namespace RevitTestFrameworkApp
{
    /// <summary>
    /// Interaction logic for View.xaml
    /// </summary>
    public partial class View : Window
    {
        public View(ViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            Closing += View_Closing;
            Loaded += View_Loaded;
        }

        void View_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModel;
        }

        private void View_Closing(object sender, CancelEventArgs e)
        {
            this.DialogResult = true;
        }

        private void TestDataTreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var vm = DataContext as ViewModel;
            vm.SelectedItem = e.NewValue;
        }
    }
}
