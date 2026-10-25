using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using static Avalonia.LinuxFramebuffer.Input.LibInput.LibInputNativeUnsafeMethods;
namespace Avalonia.LinuxFramebuffer.Input.LibInput
{
    public partial class LibInputBackend : IInputBackend
    {
        private IScreenInfoProvider? _screen;
        private IInputRoot? _inputRoot;
        private const string LibInput = nameof(LinuxFramebuffer) + "/" + nameof(Input) + "/" + nameof(LibInput);
        private Action<RawInputEventArgs>? _onInput;
        private readonly LibInputBackendOptions? _options;

        public LibInputBackend()
        {
            _options = default;
        }

        public LibInputBackend(LibInputBackendOptions options)
        {
            _options = options;
        }

        private IInputRoot InputRoot
            => _inputRoot ?? throw new InvalidOperationException($"{nameof(InputRoot)} hasn't been set");

        private unsafe void InputThread(IntPtr ctx, LibInputBackendOptions options)
        {
            var fd = libinput_get_fd(ctx);
            IntPtr[] devices = [.. options.Events!.Select(f => libinput_path_add_device(ctx, f)).Where(d => d != IntPtr.Zero)];
            var screenOrientation = _screen is ISurfaceOrientation surfaceOrientation ? surfaceOrientation.Orientation : SurfaceOrientation.Rotation0;
            
            foreach (var device in devices)
            {
                // Keep the device calibration (e.g. LIBINPUT_CALIBRATION_MATRIX set by udev)
                float[] calibration = [1, 0, 0, 0, 1, 0];
                libinput_device_config_calibration_get_default_matrix(device, calibration);
                if (GetCalibrationMatrix(screenOrientation, calibration) is { } matrix)
                    libinput_device_config_calibration_set_matrix(device, matrix);
            }

            while (true)
            {
                IntPtr ev;
                libinput_dispatch(ctx);
                while ((ev = libinput_get_event(ctx)) != IntPtr.Zero)
                {
                    var type = libinput_event_get_type(ev);

                    if (type >= LibInputEventType.LIBINPUT_EVENT_TOUCH_DOWN &&
                        type <= LibInputEventType.LIBINPUT_EVENT_TOUCH_CANCEL)
                        HandleTouch(ev, type);

                    if (type >= LibInputEventType.LIBINPUT_EVENT_POINTER_MOTION
                        && type <= LibInputEventType.LIBINPUT_EVENT_POINTER_AXIS)
                        HandlePointer(ev, type);

                    libinput_event_destroy(ev);
                    libinput_dispatch(ctx);
                }

                var pfd = new PollFd { fd = fd, events = 1 };
                NativeUnsafeMethods.poll(&pfd, new IntPtr(1), 10);
            }
        }

        /// <summary>
        /// Returns the libinput calibration matrix that applies the screen orientation on top of the device's
        /// own <paramref name="calibration"/>, or null if the calibration doesn't need to be changed.
        /// </summary>
        internal static float[]? GetCalibrationMatrix(SurfaceOrientation orientation, float[] calibration)
        {
            float[]? rotation = orientation switch
            {
                SurfaceOrientation.Rotation90 => [0, 1, 0, -1, 0, 1],
                SurfaceOrientation.Rotation180 => [-1, 0, 1, 0, -1, 1],
                SurfaceOrientation.Rotation270 => [0, -1, 1, 1, 0, 0],
                _ => null,    // Normal
            };

            if (rotation is null)
                return null;

            // rotation * calibration: the calibration is applied to the raw coordinates first, then the rotation
            return
            [
                rotation[0] * calibration[0] + rotation[1] * calibration[3],
                rotation[0] * calibration[1] + rotation[1] * calibration[4],
                rotation[0] * calibration[2] + rotation[1] * calibration[5] + rotation[2],
                rotation[3] * calibration[0] + rotation[4] * calibration[3],
                rotation[3] * calibration[1] + rotation[4] * calibration[4],
                rotation[3] * calibration[2] + rotation[4] * calibration[5] + rotation[5],
            ];
        }

        private void ScheduleInput(RawInputEventArgs ev) => _onInput?.Invoke(ev);

        public void Initialize(IScreenInfoProvider screen, Action<RawInputEventArgs> onInput)
        {
            _screen = screen;
            _onInput = onInput;
            var ctx = libinput_path_create_context();
            var options = new LibInputBackendOptions()
            {
                Events = _options?.Events is null
                    ? Directory.GetFiles("/dev/input", "event*")
                    : _options.Events,
            };
            new Thread(() => InputThread(ctx, options))
            {
                Name = "Input Manager Worker",
                IsBackground = true
            }.Start();
        }

        public void SetInputRoot(IInputRoot root)
        {
            _inputRoot = root;
        }
    }
}
