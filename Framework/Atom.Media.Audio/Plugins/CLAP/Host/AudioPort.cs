using System.Runtime.InteropServices;

namespace Atom.Media.Audio.Plugins.CLAP;

/*
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint ClapAudioPortInfoGetSupportedFormatsFn(nint port, uint index, out AudioFormat formatInfo);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate AudioFormat ClapAudioPortInfoGetFormatFn(nint port);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate bool ClapAudioPortInfoSetFormatFn(nint port, AudioFormat formatInfo);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate uint ClapAudioPortInfoGetPreferredBufferSizeFn(nint port);
*/

/// <summary>Аудиопорт CLAP-плагина (WIP).</summary>
public struct AudioPort
{
}