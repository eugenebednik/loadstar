using System.Runtime.InteropServices;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Every native call Loadstar makes, in one file on purpose.
///
/// <para>docs/anti-cheat-posture.md is a contract, and a contract nobody can check is a wish. Any
/// reviewer asking "what does this thing actually do to my machine" should be able to answer it by
/// reading one screen, so the whole P/Invoke surface is collected here rather than scattered
/// across the classes that use it. It is five functions.</para>
///
/// <para>What is deliberately absent matters more than what is present. There is no
/// <c>ReadProcessMemory</c>, no <c>OpenProcess</c>, no <c>SendInput</c>, <c>keybd_event</c>,
/// <c>PostMessage</c> or <c>SendMessage</c>, no <c>SetWindowsHookEx</c>, no
/// <c>CreateRemoteThread</c>, and nothing from d3d9/dxgi that could hook a present chain. The
/// two <c>d3d11</c> entries below create <em>our own</em> device to receive frames the compositor
/// hands us; they never touch the game's device. <c>AntiCheatPostureTests</c> asserts this by
/// scanning the compiled IL, so adding one of those functions anywhere breaks the build.</para>
///
/// <para>Window discovery does not appear here at all — it goes through
/// <see cref="System.Diagnostics.Process"/>, which needs no P/Invoke of ours and no handle to the
/// game process.</para>
/// </summary>
internal static partial class NativeMethods
{
    // ---------------------------------------------------------------------
    // combase.dll — WinRT activation.
    //
    // GraphicsCaptureItem cannot be constructed from a window handle through the projection
    // alone; the documented route is the IGraphicsCaptureItemInterop factory, and reaching a
    // factory by class name is what RoGetActivationFactory is for. This is the same path every
    // WinUI and OBS-style capture app takes.
    // ---------------------------------------------------------------------

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    internal static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    internal static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    // ---------------------------------------------------------------------
    // d3d11.dll — our own rendering device.
    //
    // Windows Graphics Capture delivers frames as Direct3D surfaces, so something has to own a
    // device to receive them into. This creates a fresh one belonging to the Loadstar process.
    // It is not the game's device, it is not shared with the game, and no game function is
    // hooked, wrapped or replaced to obtain it.
    // ---------------------------------------------------------------------

    internal const int D3D_DRIVER_TYPE_HARDWARE = 1;
    internal const int D3D_DRIVER_TYPE_WARP = 5;

    /// <summary>Required for interop with the BGRA surfaces the capture API produces.</summary>
    internal const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

    internal const uint D3D11_SDK_VERSION = 7;

    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [LibraryImport("d3d11.dll")]
    internal static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    // ---------------------------------------------------------------------
    // COM interfaces. Not P/Invokes — vtable calls on objects the OS hands us.
    // ---------------------------------------------------------------------

    internal static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>IID of <c>IGraphicsCaptureItem</c>, which is what the interop factory returns.</summary>
    internal static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>
    /// The documented desktop-interop escape hatch on the GraphicsCaptureItem activation factory.
    /// <c>CreateForWindow</c> is how a Win32 app names the window it wants frames for — it asks
    /// the compositor for that window's output and gets nothing else.
    /// </summary>
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    /// <summary>
    /// Gives direct access to the bytes behind a locked <c>SoftwareBitmap</c>, so a frame can be
    /// read without a per-pixel round trip through the projection.
    /// </summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
