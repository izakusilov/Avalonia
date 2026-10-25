using Avalonia.Threading;
using Avalonia.UnitTests;
using Avalonia.Wayland.Screens;
using NWayland.Protocols.Wayland;
using Xunit;

namespace Avalonia.Wayland.UnitTests;

public class SnapshotScreensImplTests : ScopedTestBase
{
    [Fact]
    public void Initial_Snapshot_Is_Visible_Before_Dispatcher_Runs()
    {
        using var _ = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        var output = new object();
        var screens = new SnapshotScreensImpl();
        SimulatePlatformInit(screens, output);

        Assert.Equal(1, screens.ScreenCount);
        Assert.Equal(new PixelRect(0, 0, 1280, 800), screens.AllScreens[0].Bounds);
    }

    [Fact]
    public void Snapshots_Queued_During_Init_Do_Not_Roll_Back_Initial_Snapshot()
    {
        using var _ = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        var output = new object();
        var screens = new SnapshotScreensImpl();
        var changed = 0;
        screens.Changed = () => changed++;
        SimulatePlatformInit(screens, output);
        var screen = Assert.Single(screens.AllScreens);

        Dispatcher.UIThread.RunJobs();

        Assert.Same(screen, Assert.Single(screens.AllScreens));
        Assert.Equal(new PixelRect(0, 0, 1280, 800), screen.Bounds);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Snapshot_Newer_Than_Initial_Updates_Screens()
    {
        using var _ = UnitTestApplication.Start(TestServices.MockThreadingInterface);

        var output = new object();
        var screens = new SnapshotScreensImpl();
        SimulatePlatformInit(screens, output);
        Dispatcher.UIThread.RunJobs();
        var changed = 0;
        screens.Changed = () => changed++;

        screens.OnOutputsChanged(new WaylandOutputsSnapshot(4,
            [CreateOutput(output, 0, 1280, 800), CreateOutput(new object(), 1280, 1920, 1080)]));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, screens.ScreenCount);
        Assert.Equal(new PixelRect(1280, 0, 1920, 1080), screens.AllScreens[1].Bounds);
        Assert.Equal(1, changed);
    }

    private static void SimulatePlatformInit(SnapshotScreensImpl screens, object output)
    {
        // During the initial roundtrips the worker posts a snapshot on every wl_output.done:
        // first from wl_output events only, then again once zxdg_output_v1 data has arrived.
        var sink = new WaylandOutputsSinkProxy(screens, Dispatcher.UIThread.Post);
        sink.OnOutputsChanged(new WaylandOutputsSnapshot(1, [CreateOutput(output, 0, 1920, 1200)]));
        sink.OnOutputsChanged(new WaylandOutputsSnapshot(2, [CreateOutput(output, 0, 1280, 800)]));

        // Then the platform applies the outputs known at the end of init.
        screens.ApplyInitialSnapshot(new WaylandOutputsSnapshot(3, [CreateOutput(output, 0, 1280, 800)]));
    }

    private static WaylandOutputSnapshot CreateOutput(object id, int x, int width, int height) =>
        new(id, "output-" + x, null, null, null, new PixelPoint(x, 0), new PixelSize(width, height), 1, 60,
            WlOutput.SubpixelEnum.Unknown, WlOutput.TransformEnum.Normal, default);
}
