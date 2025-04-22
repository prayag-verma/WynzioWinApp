using System;
using System.ComponentModel;
using System.Windows;
using Wynzio.ViewModels;

namespace Wynzio.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Changed from readonly to allow assignment in Window_Loaded
        private MainViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Handle window loaded event
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the view model from DataContext
            _viewModel = DataContext as MainViewModel;

            if (_viewModel != null)
            {
                // Update initial status in tray menu
                UpdateTrayMenuStatus(_viewModel.StatusMessage, _viewModel.HostId);

                // Subscribe to property changed events
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // Update auto-start menu item
                if (miAutoStart != null)
                {
                    miAutoStart.IsChecked = _viewModel.IsAutoStartEnabled;
                }
            }
        }

        /// <summary>
        /// Handle property changed events from view model
        /// </summary>
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Update status in tray menu
            if (e.PropertyName == nameof(MainViewModel.StatusMessage) ||
                e.PropertyName == nameof(MainViewModel.HostId))
            {
                if (_viewModel != null)
                {
                    UpdateTrayMenuStatus(_viewModel.StatusMessage, _viewModel.HostId);
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.IsAutoStartEnabled))
            {
                if (_viewModel != null && miAutoStart != null)
                {
                    miAutoStart.IsChecked = _viewModel.IsAutoStartEnabled;
                }
            }
        }

        /// <summary>
        /// Update status text in the tray context menu
        /// </summary>
        private void UpdateTrayMenuStatus(string status, string hostId)
        {
            // Update status menu item
            if (miStatus != null)
            {
                miStatus.Header = $"Status: {status}";
            }

            // Update host ID menu item
            if (miHostId != null)
            {
                miHostId.Header = $"Host ID: {hostId}";
            }

            // Update tray icon tooltip
            if (notifyIcon != null)
            {
                notifyIcon.ToolTipText = $"Wynzio Remote Access\nHost ID: {hostId}\nStatus: {status}";
            }
        }

        /// <summary>
        /// Handle tray icon click
        /// </summary>
        private void NotifyIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            // Show context menu on left click (same as right click)
            if (notifyIcon.ContextMenu != null)
            {
                notifyIcon.ContextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// Handle window closing event
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Prevent window from closing
            // Instead, hide the window and keep the application running
            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// Start service click handler
        /// </summary>
        private void StartService_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.StartCommand.CanExecute(null))
            {
                _viewModel.StartCommand.Execute(null);
            }
        }

        /// <summary>
        /// Stop service click handler
        /// </summary>
        private void StopService_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.StopCommand.CanExecute(null))
            {
                _viewModel.StopCommand.Execute(null);
            }
        }

        /// <summary>
        /// Auto-start toggle click handler
        /// </summary>
        private void AutoStart_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.IsAutoStartEnabled = !_viewModel.IsAutoStartEnabled;
            }
        }

        /// <summary>
        /// Exit application click handler
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && _viewModel.ExitCommand.CanExecute(null))
            {
                _viewModel.ExitCommand.Execute(null);
            }
        }
    }
}