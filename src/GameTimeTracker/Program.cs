using System;
using System.Windows.Forms;

namespace GameTimeTracker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var store = new SqliteGameStore(AppPaths.DatabaseFilePath, AppPaths.DataFilePath);
        var state = store.Load();
        var tracker = new TrackingEngine(state, store);
        Application.Run(new MainForm(state, tracker, store));
    }
}
