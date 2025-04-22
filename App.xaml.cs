using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Wynzio.Services.Capture;
using Wynzio.Services.Input;
using Wynzio.Services.Network;
using Wynzio.Services.Security;
using Wynzio.ViewModels;
using Wynzio.Views;
using Serilog;
using Serilog.Events;

namespace Wynzio
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Initialize as nullable to fix CS8618 warning
        private readonly IHost? _host;
        private readonly Mutex _instanceMutex;
        private readonly bool _isNewInstance;

        public App()
        {
            // Setup unhandled exception handling
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Current.DispatcherUnhandledException += Application_DispatcherUnhandledException;

            // Ensure only one instance of the application runs
            _instanceMutex = new Mutex(true, "Wynzio_SingleInstance_Mutex", out _isNewInstance);

            if (!_isNewInstance)
            {
                // If an instance is already running, exit this instance
                MessageBox.Show("Wynzio is already running in the background.", "Wynzio",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Configure logger
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File("logs/wynzio-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            try
            {
                Log.Information("Starting Wynzio");

                // Build host with services
                _host = Host.CreateDefaultBuilder()
                    .UseSerilog()
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureServices(services);
                    })
                    .Build();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                MessageBox.Show($"Failed to start application: {ex.Message}", "Wynzio Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>
        /// Handle unhandled exceptions from the UI thread
        /// </summary>
        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception
            Log.Error(e.Exception, "Unhandled UI exception");

            // Mark as handled to prevent application crash
            e.Handled = true;

            // Only show message box for critical errors, not for WebSocket connection errors
            if (e.Exception is not System.Net.WebSockets.WebSocketException)
            {
                MessageBox.Show($"An error occurred: {e.Exception.Message}", "Wynzio Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Handle unhandled exceptions from non-UI threads
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
            {
                // Log the exception
                Log.Fatal(exception, "Unhandled application exception");
            }
            else
            {
                Log.Fatal("Unknown unhandled application exception");
            }

            // If terminating, show error and exit gracefully
            if (e.IsTerminating)
            {
                MessageBox.Show("A critical error occurred and the application needs to close.",
                    "Wynzio Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Ensure clean shutdown
                try
                {
                    _host?.StopAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                    _host?.Dispose();
                }
                catch
                {
                    // Ignore errors during emergency shutdown
                }

                Shutdown(1);
            }
        }

        /// <summary>
        /// Configure dependency injection services
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // Register services
            services.AddSingleton<INetworkStatusService, NetworkStatusService>();
            services.AddSingleton<IScreenCaptureService, CaptureService>();
            services.AddSingleton<IInputService, InputService>();
            services.AddSingleton<ISignalingService, SignalingService>();
            services.AddSingleton<ISecurityService, SecurityService>();
            services.AddSingleton<IWebRTCService, WebRTCService>();

            // Register view models
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<ConnectionViewModel>();
            services.AddSingleton<SettingsViewModel>();

            // Register views
            services.AddSingleton<MainWindow>();
        }

        /// <summary>
        /// Application startup event handler
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            if (!_isNewInstance)
                return;

            try
            {
                await _host!.StartAsync();

                // Start network monitoring service
                var networkService = _host.Services.GetRequiredService<INetworkStatusService>();
                networkService.StartMonitoring();

                // Show main window - it's hidden by default but we create it
                // so the taskbar icon is initialized
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
                mainWindow.Show();

                // Main window hides itself after initialization

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed during startup");
                MessageBox.Show($"Failed to start application: {ex.Message}", "Wynzio Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>
        /// Application exit event handler
        /// </summary>
        protected override async void OnExit(ExitEventArgs e)
        {
            if (_isNewInstance)
            {
                try
                {
                    // Stop network monitoring
                    if (_host != null)
                    {
                        var networkService = _host.Services.GetService<INetworkStatusService>();
                        if (networkService != null)
                        {
                            networkService.StopMonitoring();
                        }
                    }

                    // Stop the host gracefully
                    if (_host != null)
                    {
                        await _host.StopAsync(TimeSpan.FromSeconds(5));
                        _host.Dispose();
                    }

                    // Release mutex
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();

                    Log.Information("Application shutdown complete");
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Error during application shutdown");
                }
                finally
                {
                    Log.CloseAndFlush();
                }
            }

            base.OnExit(e);
        }
    }
}