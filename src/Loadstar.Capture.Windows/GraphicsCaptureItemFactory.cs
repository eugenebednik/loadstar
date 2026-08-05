using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace Loadstar.Capture.Windows;

/// <summary>
/// Builds a <see cref="GraphicsCaptureItem"/> for a window handle.
///
/// <para>The projection has no constructor that takes an HWND, because WinRT has no concept of
/// one. The documented bridge for desktop apps is the <c>IGraphicsCaptureItemInterop</c> face of
/// the class's activation factory, which is what this reaches through.</para>
///
/// <para>Nothing here grants access to the window's process. The handle names <em>which</em>
/// output the compositor should copy for us, and that is the entire extent of it.</para>
/// </summary>
internal static class GraphicsCaptureItemFactory
{
    private const string ClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle is null.", nameof(hwnd));
        }

        var interop = GetInterop();
        var iid = NativeMethods.IID_IGraphicsCaptureItem;

        IntPtr abi;

        try
        {
            abi = interop.CreateForWindow(hwnd, ref iid);
        }
        catch (COMException ex)
        {
            throw new CaptureException(
                "Windows refused to create a capture item for that window. This happens when the " +
                "window has gone away, or when it belongs to a process at a higher integrity level " +
                "than Loadstar. Running the game as administrator while Loadstar is not is the " +
                "usual cause.",
                ex);
        }

        if (abi == IntPtr.Zero)
        {
            throw new CaptureException("Windows returned no capture item for that window.");
        }

        try
        {
            return GraphicsCaptureItem.FromAbi(abi);
        }
        finally
        {
            // FromAbi took its own reference; release the one CreateForWindow handed us.
            Marshal.Release(abi);
        }
    }

    private static NativeMethods.IGraphicsCaptureItemInterop GetInterop()
    {
        var hr = NativeMethods.WindowsCreateString(ClassName, ClassName.Length, out var classId);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            var iid = typeof(NativeMethods.IGraphicsCaptureItemInterop).GUID;
            hr = NativeMethods.RoGetActivationFactory(classId, in iid, out var factory);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                return (NativeMethods.IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            NativeMethods.WindowsDeleteString(classId);
        }
    }
}
