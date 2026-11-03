using System.Runtime.InteropServices;
using Captly.Core.Speech;

namespace Captly.Windowing;

/// <summary>
/// Lists the PC's graphics cards through DXGI, for the Speech Model screen's Graphics card select.
/// No model can use one yet (D3), but the controls stay (D22). Calls the COM interfaces by vtable
/// slot, which needs no interop library for two methods.
/// </summary>
public static unsafe class GraphicsAdapters
{
    private const uint SoftwareAdapter = 2;
    private const int ReleaseSlot = 2;
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    /// <summary>Hardware adapters only. Ids are vendor and device numbers, which, unlike a LUID, survive a restart.</summary>
    public static IReadOnlyList<GraphicsAdapter> List()
    {
        var adapters = new List<GraphicsAdapter>();
        if (CreateDXGIFactory1(FactoryId, out var factory) < 0) return adapters;
        try
        {
            var enumAdapters = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(nint**)factory)[EnumAdapters1Slot];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                if (enumAdapters(factory, i, &adapter) < 0) break;
                try
                {
                    var getDesc = (delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)(*(nint**)adapter)[GetDesc1Slot];
                    AdapterDesc1 desc;
                    if (getDesc(adapter, &desc) < 0 || (desc.Flags & SoftwareAdapter) != 0) continue;
                    var id = $"{desc.VendorId:X4}:{desc.DeviceId:X4}";
                    if (adapters.Any(a => a.Id == id)) continue;
                    adapters.Add(new GraphicsAdapter(id, new string(desc.Description).Trim(), (long)desc.DedicatedVideoMemory));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        return adapters;
    }

    private static void Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[ReleaseSlot])(unknown);

    [DllImport("dxgi")] private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);
}
