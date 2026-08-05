using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Loadstar.Capture.Windows;

/// <summary>
/// The Direct3D device frames are delivered into.
///
/// <para>Worth being precise about what this is, because "the overlay creates a D3D device" sounds
/// like the thing the posture document forbids and is not. Windows Graphics Capture hands out
/// frames as Direct3D surfaces, so the receiving process needs a device of its own to hold them.
/// This creates one belonging to Loadstar. It never touches the game's device, swap chain or
/// present call — the compositor does the copying, which is precisely why this approach is
/// allowed and renderer hooking is not.</para>
/// </summary>
internal sealed class CaptureDevice : IDisposable
{
    private IntPtr _d3dDevice;
    private IntPtr _immediateContext;
    private bool _disposed;

    public IDirect3DDevice Device { get; }

    /// <summary>True when we fell back to the software rasteriser because no GPU device was available.</summary>
    public bool IsSoftware { get; }

    private CaptureDevice(IntPtr d3dDevice, IntPtr immediateContext, IDirect3DDevice device, bool isSoftware)
    {
        _d3dDevice = d3dDevice;
        _immediateContext = immediateContext;
        Device = device;
        IsSoftware = isSoftware;
    }

    public static CaptureDevice Create()
    {
        // Hardware first. WARP is a real fallback rather than a nicety: remote sessions and some
        // VM configurations have no usable adapter, and a software device still captures
        // correctly — just slower, which is irrelevant at one frame every couple of minutes.
        var hr = CreateRaw(NativeMethods.D3D_DRIVER_TYPE_HARDWARE, out var d3dDevice, out var context);
        var isSoftware = false;

        if (hr < 0)
        {
            hr = CreateRaw(NativeMethods.D3D_DRIVER_TYPE_WARP, out d3dDevice, out context);
            isSoftware = true;
        }

        if (hr < 0)
        {
            throw new CaptureException($"Could not create a Direct3D 11 device (HRESULT 0x{hr:X8}).");
        }

        try
        {
            return new CaptureDevice(d3dDevice, context, WrapAsWinRt(d3dDevice), isSoftware);
        }
        catch
        {
            Release(ref d3dDevice);
            Release(ref context);
            throw;
        }
    }

    private static int CreateRaw(int driverType, out IntPtr device, out IntPtr context) =>
        NativeMethods.D3D11CreateDevice(
            adapter: IntPtr.Zero,
            driverType: driverType,
            software: IntPtr.Zero,
            flags: NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels: IntPtr.Zero,
            featureLevelCount: 0,
            sdkVersion: NativeMethods.D3D11_SDK_VERSION,
            device: out device,
            featureLevel: out _,
            immediateContext: out context);

    /// <summary>
    /// Bridges the raw COM device to the WinRT <see cref="IDirect3DDevice"/> the capture API wants.
    /// Two hops: query for the DXGI face of the device, then let the OS wrap it.
    /// </summary>
    private static IDirect3DDevice WrapAsWinRt(IntPtr d3dDevice)
    {
        var dxgiIid = NativeMethods.IID_IDXGIDevice;
        var hr = Marshal.QueryInterface(d3dDevice, ref dxgiIid, out var dxgiDevice);

        if (hr < 0)
        {
            throw new CaptureException($"Direct3D device does not expose IDXGIDevice (HRESULT 0x{hr:X8}).");
        }

        try
        {
            hr = NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable);

            if (hr < 0)
            {
                throw new CaptureException($"Could not project the Direct3D device into WinRT (HRESULT 0x{hr:X8}).");
            }

            try
            {
                // FromAbi takes its own reference, so ours is still ours to release.
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Release(ref inspectable);
            }
        }
        finally
        {
            Release(ref dxgiDevice);
        }
    }

    private static void Release(ref IntPtr unknown)
    {
        if (unknown != IntPtr.Zero)
        {
            Marshal.Release(unknown);
            unknown = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Device is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Release(ref _immediateContext);
        Release(ref _d3dDevice);
    }
}

/// <summary>Raised when the capture stack itself fails, as opposed to simply finding no frame.</summary>
public sealed class CaptureException : Exception
{
    public CaptureException(string message, Exception? inner = null) : base(message, inner) { }
}
