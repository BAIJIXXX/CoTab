using System.Threading;
using System.Windows;
using CoTab.Helpers;
using CoTab.Services;

namespace CoTab;

public partial class App : Application
{
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
            StateStore.Log("未处理异常: " + args.Exception);
            MessageBox.Show("CoTab 遇到了一个问题，但会尝试继续运行。\n\n" + args.Exception.Message,
                "CoTab", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var previewPath = GetArgument(e.Args, "--preview");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            var scene = GetArgument(e.Args, "--scene") ?? "board";
            PreviewRenderer.Run(previewPath, scene);
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, @"Local\CoTab.StickyBoard.7F3A", out var createdNew);
        if (!createdNew)
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        var state = StateStore.Load();
        var window = new MainWindow(state);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch
        {
            // Not the owner; nothing to release.
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            var handle = NativeMethods.FindWindow(null, "CoTab 便签板");
            if (handle == IntPtr.Zero) return;
            if (NativeMethods.IsIconic(handle))
                NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
            else
                NativeMethods.ShowWindow(handle, NativeMethods.SwShow);
            NativeMethods.PostMessage(handle, CoTab.MainWindow.ShowRequestMessage, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SetForegroundWindow(handle);
        }
        catch
        {
            // Nothing useful to do if activation fails.
        }
    }
}
