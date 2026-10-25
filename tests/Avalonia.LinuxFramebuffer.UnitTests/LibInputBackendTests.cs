using Avalonia.LinuxFramebuffer.Input.LibInput;
using Avalonia.Platform;
using Xunit;

namespace Avalonia.LinuxFramebuffer.UnitTests;

public class LibInputBackendTests
{
    private static readonly float[] s_identity = [1, 0, 0, 0, 1, 0];

    // LIBINPUT_CALIBRATION_MATRIX from the udev rule in #21716
    private static readonly float[] s_udevCalibration = [-1.15f, 0, 1.075f, 0, -1.15f, 1.075f];

    // Unlike the one above, doesn't commute with the rotations, so it also checks the order they are combined in
    private static readonly float[] s_skewedCalibration = [1.1f, 0.02f, -0.03f, -0.01f, 0.9f, 0.07f];

    public static TheoryData<float[]> Calibrations => new() { s_identity, s_udevCalibration };

    public static TheoryData<SurfaceOrientation, float[]> RotatedCalibrations
    {
        get
        {
            var data = new TheoryData<SurfaceOrientation, float[]>();
            foreach (var orientation in new[] { SurfaceOrientation.Rotation90, SurfaceOrientation.Rotation180, SurfaceOrientation.Rotation270 })
            {
                data.Add(orientation, s_udevCalibration);
                data.Add(orientation, s_skewedCalibration);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Calibrations))]
    public void Calibration_Is_Not_Changed_Without_Rotation(float[] calibration)
    {
        Assert.Null(LibInputBackend.GetCalibrationMatrix(SurfaceOrientation.Rotation0, calibration));
    }

    [Theory]
    [InlineData(SurfaceOrientation.Rotation90, new float[] { 0, 1, 0, -1, 0, 1 })]
    [InlineData(SurfaceOrientation.Rotation180, new float[] { -1, 0, 1, 0, -1, 1 })]
    [InlineData(SurfaceOrientation.Rotation270, new float[] { 0, -1, 1, 1, 0, 0 })]
    public void Uncalibrated_Device_Gets_Rotation_Matrix(SurfaceOrientation orientation, float[] expected)
    {
        Assert.Equal(expected, LibInputBackend.GetCalibrationMatrix(orientation, s_identity));
    }

    [Fact]
    public void Rotation_Is_Combined_With_Device_Calibration()
    {
        var matrix = LibInputBackend.GetCalibrationMatrix(SurfaceOrientation.Rotation180, s_udevCalibration);

        AssertEqual([1.15f, 0, -0.075f, 0, 1.15f, -0.075f], matrix);
    }

    [Theory]
    [MemberData(nameof(RotatedCalibrations))]
    public void Calibrated_Point_Is_Rotated(SurfaceOrientation orientation, float[] calibration)
    {
        var rotation = LibInputBackend.GetCalibrationMatrix(orientation, s_identity)!;
        var matrix = LibInputBackend.GetCalibrationMatrix(orientation, calibration)!;

        // libinput applies the matrix to normalized device coordinates
        foreach (var point in new[] { new Point(0.1, 0.2), new Point(0.5, 0.5), new Point(0.9, 0.3) })
        {
            var expected = Transform(rotation, Transform(calibration, point));
            var actual = Transform(matrix, point);

            Assert.Equal(expected.X, actual.X, 5);
            Assert.Equal(expected.Y, actual.Y, 5);
        }
    }

    private static Point Transform(float[] m, Point p) =>
        new(m[0] * p.X + m[1] * p.Y + m[2], m[3] * p.X + m[4] * p.Y + m[5]);

    private static void AssertEqual(float[] expected, float[]? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], 5);
    }
}
