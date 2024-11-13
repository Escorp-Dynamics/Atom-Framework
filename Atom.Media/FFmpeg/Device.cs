using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

internal static partial class FFmpeg
{
    public static partial class Device
    {
        private const string Dll = "avdevice";

        [LibraryImport(Dll, EntryPoint = "avdevice_register_all", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static partial void RegisterAll();
    }
}