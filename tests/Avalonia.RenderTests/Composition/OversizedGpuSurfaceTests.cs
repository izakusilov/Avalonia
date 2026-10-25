#if AVALONIA_SKIA
using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Surfaces;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.UnitTests;
using SkiaSharp;
using Xunit;
using static Avalonia.OpenGL.GlConsts;

namespace Avalonia.Skia.RenderTests;

public class OversizedGpuSurfaceTests : TestBase
{
    public OversizedGpuSurfaceTests() : base("Composition/OversizedGpuSurface") { }

    // Mesa's llvmpipe has a max render target size of 16384
    [Theory]
    [InlineData(16385, 2, true)]
    [InlineData(2, 16385, true)]
    [InlineData(16384, 2, false)]
    public void Surface_Larger_Than_Max_Gpu_Render_Target_Is_Rendered_Via_Framebuffer_Surface(int width, int height,
        bool expectFallback)
    {
        if (!MesaSoftwareRenderer.GlEnabled)
            return;
        var gpu = MesaSoftwareRenderer.Gl;
        var timer = new ManualRenderTimer();
        var compositor = new Compositor(RenderLoop.FromTimer(timer), gpu, true,
            new DispatcherCompositorScheduler(), true, Dispatcher.UIThread);
        var glSurface = new WindowGlSurface();
        using var fbSurface = new BitmapFramebufferSurface(new PixelSize(width, height));
        var root = new TestRenderRoot(1, null!);
        var renderer = new CompositingRenderer(root, compositor,
            () => new IPlatformRenderSurface[] { glSurface, fbSurface });
        try
        {
            root.Initialize(renderer, new Border { Width = width, Height = height, Background = Brushes.Red });
            renderer.Start();
            Dispatcher.UIThread.RunJobs(null, TestContext.Current.CancellationToken);
            timer.TriggerTick();

            if (expectFallback)
            {
                Assert.Equal(SKColors.Red, fbSurface.Bitmap.GetPixel(width - 1, height - 1));
                Assert.Equal(1, fbSurface.LockCount);
                Assert.Equal(0, glSurface.BeginDrawCount);
            }
            else
            {
                Assert.Equal(1, glSurface.BeginDrawCount);
                Assert.Equal(0, fbSurface.LockCount);
            }
        }
        finally
        {
            root.Child = null;
            Dispatcher.UIThread.RunJobs(null, TestContext.Current.CancellationToken);
            renderer.Dispose();
            var renderInterface = compositor.Server.RenderInterface;
            using (renderInterface.EnsureCurrent())
                compositor.Server.ResetAllGpuResources();
            if (!gpu.UsesSharedContext)
                renderInterface.GpuContext?.Dispose();
        }
    }

    // Like a window, renders to the default framebuffer, which Skia can wrap at any size
    private sealed class WindowGlSurface : IGlPlatformSurface
    {
        public int BeginDrawCount;

        public IGlPlatformSurfaceRenderTarget CreateGlRenderTarget(IGlContext context) => new Target(this, context);

        private sealed class Target : IGlPlatformSurfaceRenderTarget
        {
            private readonly WindowGlSurface _owner;
            private readonly IGlContext _context;

            public Target(WindowGlSurface owner, IGlContext context)
            {
                _owner = owner;
                _context = context;
            }

            public IGlPlatformSurfaceRenderingSession BeginDraw(IRenderTarget.RenderTargetSceneInfo sceneInfo)
            {
                _owner.BeginDrawCount++;
                var restoreContext = _context.MakeCurrent();
                _context.GlInterface.BindFramebuffer(GL_FRAMEBUFFER, 0);
                return new Session(_context, sceneInfo.Size, restoreContext);
            }

            public void Dispose()
            {
            }
        }

        private sealed class Session : IGlPlatformSurfaceRenderingSession
        {
            private readonly IDisposable _restoreContext;

            public Session(IGlContext context, PixelSize size, IDisposable restoreContext)
            {
                Context = context;
                Size = size;
                _restoreContext = restoreContext;
            }

            public IGlContext Context { get; }
            public PixelSize Size { get; }
            public double Scaling => 1;
            public bool IsYFlipped => false;

            public void Dispose() => _restoreContext.Dispose();
        }
    }

    private sealed class BitmapFramebufferSurface : IFramebufferPlatformSurface, IDisposable
    {
        public readonly SKBitmap Bitmap;
        public int LockCount;

        public BitmapFramebufferSurface(PixelSize size)
        {
            Bitmap = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        }

        public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new FuncFramebufferRenderTarget(() =>
        {
            LockCount++;
            return new LockedFramebuffer(Bitmap.GetAddress(0, 0), new PixelSize(Bitmap.Width, Bitmap.Height),
                Bitmap.RowBytes, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul, null);
        });

        public void Dispose() => Bitmap.Dispose();
    }
}
#endif
