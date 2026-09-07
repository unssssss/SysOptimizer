using System.Collections.Generic;
using System.Threading;
using System.Windows;
using SysOptimizer.Models;

namespace SysOptimizer.UI;

/// <summary>
/// wpf windows need to run on an sta thread, and our Main is async which doesn't
/// guarantee that. easiest fix i found: spin up a dedicated sta thread just for the
/// dialog, show it, grab the result, and let the thread end. the console app never
/// has to become a full wpf app just for one window
/// </summary>
public static class GuiApproval
{
    public static List<string> ShowApprovalDialog(IEnumerable<Recommendation> recommendations)
    {
        List<string> approvedIds = new();

        var thread = new Thread(() =>
        {
            // an Application instance is needed for wpf resources/dispatcher stuff to
            // work at all, even though we're not calling app.Run() — we're just using
            // ShowDialog directly since we only ever need this one window
            var app = new Application();
            var window = new ApprovalWindow(recommendations);
            window.ShowDialog();
            approvedIds = window.ApprovedIds;
            app.Shutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return approvedIds;
    }
}
