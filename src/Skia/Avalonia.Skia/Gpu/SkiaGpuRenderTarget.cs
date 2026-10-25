using System;
using Avalonia.Logging;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace Avalonia.Skia
{
    /// <summary>
    /// Adapts <see cref="ISkiaGpuRenderTarget"/> to be used within our rendering pipeline.
    /// </summary>
    internal class SkiaGpuRenderTarget : IRenderTarget
    {
        private readonly ISkiaGpu _skiaGpu;
        private readonly ISkiaGpuRenderTarget _renderTarget;
        private readonly IFramebufferPlatformSurface? _fallbackSurface;
        private readonly PixelSize? _maxSize;
        private FramebufferRenderTarget? _fallbackRenderTarget;

        public SkiaGpuRenderTarget(ISkiaGpu skiaGpu, ISkiaGpuRenderTarget renderTarget,
            IFramebufferPlatformSurface? fallbackSurface, PixelSize? maxSize)
        {
            _skiaGpu = skiaGpu;
            _renderTarget = renderTarget;
            _fallbackSurface = fallbackSurface;
            _maxSize = maxSize;
        }

        public void Dispose()
        {
            _renderTarget.Dispose();
            _fallbackRenderTarget?.Dispose();
            _fallbackRenderTarget = null;
        }

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing) => throw new InvalidOperationException(
            "This legacy API is only supported by framebuffer render targets and should be removed from there as well, don't use");

        public IDrawingContextImpl CreateDrawingContext(IRenderTarget.RenderTargetSceneInfo sceneInfo,
            out RenderTargetDrawingContextProperties properties)
        {
            // The GPU can't render to a surface larger than its max render target size (only 4096 on some devices),
            // so render such a surface in software if the platform has a framebuffer surface for it
            if (_fallbackSurface != null && _maxSize is { } maxSize
                && (sceneInfo.Size.Width > maxSize.Width || sceneInfo.Size.Height > maxSize.Height))
            {
                if (_fallbackRenderTarget == null)
                {
                    Logger.TryGet(LogEventLevel.Warning, LogArea.Visual)?.Log(this,
                        "Surface size {Size} exceeds the maximum GPU render target size {MaxSize}, using software rendering",
                        sceneInfo.Size, maxSize);
                    _fallbackRenderTarget = new FramebufferRenderTarget(_fallbackSurface);
                }

                return _fallbackRenderTarget.CreateDrawingContext(sceneInfo, out properties);
            }

            _fallbackRenderTarget?.Dispose();
            _fallbackRenderTarget = null;

            properties = default;
            var session = _renderTarget.BeginRenderingSession(sceneInfo);

            var nfo = new DrawingContextImpl.CreateInfo
            {
                GrContext = session.GrContext,
                Surface = session.SkSurface,
                Dpi = SkiaPlatform.DefaultDpi * session.ScaleFactor,
                ScaleDrawingToDpi = false,
                Gpu = _skiaGpu,
                CurrentSession =  session
            };

            return new DrawingContextImpl(nfo, session);
        }
        
        // A corrupted GPU target still needs to be recreated while the software fallback is in use
        public PlatformRenderTargetState PlatformRenderTargetState =>
            _fallbackRenderTarget == null || _renderTarget.State.IsCorrupted
                ? _renderTarget.State
                : _fallbackRenderTarget.PlatformRenderTargetState;
        public RenderTargetProperties Properties => _fallbackRenderTarget?.Properties ?? _renderTarget.Properties;


    }
}
