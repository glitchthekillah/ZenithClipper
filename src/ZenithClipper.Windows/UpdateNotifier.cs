using System.Diagnostics;

namespace ZenithClipper.Windows;

/// <summary>Background update check (at sign-in and from the daily scheduled task). If a newer version is out and the user
/// hasn't been told about it yet, shows a Windows notification; clicking it opens the panel and installs the update.
/// The process lives only while the notification is up, then exits.</summary>
static class UpdateNotifier
{
    public static void Run(bool force)
    {
        if (!force && !Updates.Due(20)) return;
        var update = Updates.CheckAsync().GetAwaiter().GetResult();
        if (update is null || Updates.State.Notified == update.Tag) return;
        Updates.SaveState(Updates.State with { Notified = update.Tag });
        using var icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "ZenithClipper update available", Visible = true,
            BalloonTipTitle = $"ZenithClipper {update.Version.ToString(3)} is available",
            BalloonTipText = Updates.CanSelfUpdate ? "Click to update now. It only takes a moment." : "Click to open the download page.",
            BalloonTipIcon = ToolTipIcon.Info,
        };
        using var done = new System.Windows.Forms.Timer { Interval = 60_000 };
        void Finish() { icon.Visible = false; Application.ExitThread(); }
        icon.BalloonTipClicked += (_, _) =>
        {
            if (Updates.CanSelfUpdate) Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "update" } });
            else Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            Finish();
        };
        icon.BalloonTipClosed += (_, _) => Finish();
        icon.Click += (_, _) => { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, ArgumentList = { "update" } }); Finish(); };
        done.Tick += (_, _) => Finish();
        icon.ShowBalloonTip(15_000); done.Start();
        Application.Run();
    }
}
