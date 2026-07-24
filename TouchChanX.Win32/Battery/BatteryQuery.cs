using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TouchChanX.Win32.Battery;

/// <summary>
/// Raw battery metrics from the Windows battery class driver (IOCTL).
/// Ported from TachiChan BatteryInformation.
/// </summary>
public readonly record struct BatteryRawInfo(
    int DesignedMaxCapacityMwh,
    int FullChargeCapacityMwh,
    int CurrentCapacityMwh,
    int DischargeRateMw,
    uint VoltageMv);

/// <summary>
/// Queries battery presence and status via SetupAPI + battery IOCTLs.
/// </summary>
public static class BatteryQuery
{
    // GUID_DEVCLASS_BATTERY
    private static readonly Guid BatteryClassGuid = new(0x72631E54, 0x78A4, 0x11D0, 0xBC, 0xF7, 0x00, 0xAA, 0x00, 0xB7, 0xB3, 0x2A);

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;

    // CTL_CODE(FILE_DEVICE_BATTERY=0x29, function, METHOD_BUFFERED=0, FILE_READ_ACCESS=1)
    // Matches TachiChan: 0x29 << 16 | FileAccess.Read << 14 | function << 2
    private const uint IoctlBatteryQueryTag = 0x00294040;          // function 0x10
    private const uint IoctlBatteryQueryInformation = 0x00294044;  // function 0x11
    private const uint IoctlBatteryQueryStatus = 0x0029404C;       // function 0x13

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    public static bool IsAvailable()
    {
        var guid = BatteryClassGuid;
        var deviceList = SetupDiGetClassDevs(ref guid, null, nint.Zero, DigcfPresent | DigcfDeviceInterface);
        if (IsInvalidDeviceList(deviceList))
            return false;

        try
        {
            var interfaceData = CreateInterfaceData();
            return SetupDiEnumDeviceInterfaces(deviceList, nint.Zero, ref guid, 0, ref interfaceData);
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceList);
        }
    }

    public static bool IsAcPowerOnline()
    {
        if (!GetSystemPowerStatus(out var status))
            return false;

        // 0 = offline (on battery), 1 = online (AC), 255 = unknown
        return status.ACLineStatus == 1;
    }

    public static bool TryGetInfo(out BatteryRawInfo info)
    {
        info = default;
        nint deviceList = nint.Zero;
        SafeFileHandle? batteryHandle = null;

        try
        {
            var guid = BatteryClassGuid;
            deviceList = SetupDiGetClassDevs(ref guid, null, nint.Zero, DigcfPresent | DigcfDeviceInterface);
            if (IsInvalidDeviceList(deviceList))
                return false;

            var interfaceData = CreateInterfaceData();
            if (!SetupDiEnumDeviceInterfaces(deviceList, nint.Zero, ref guid, 0, ref interfaceData))
                return false;

            // Probe required size, then fetch device path (same flow as TachiChan).
            SetupDiGetDeviceInterfaceDetail(
                deviceList, ref interfaceData, nint.Zero, 0, out var requiredSize, nint.Zero);
            if (requiredSize == 0)
                return false;

            var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize:
                // 8 on 64-bit, 4 + sizeof(TCHAR) on 32-bit.
                Marshal.WriteInt32(detailBuffer, nint.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                if (!SetupDiGetDeviceInterfaceDetail(
                        deviceList,
                        ref interfaceData,
                        detailBuffer,
                        requiredSize,
                        out _,
                        nint.Zero))
                {
                    return false;
                }

                // DevicePath is a variable-length string that begins at offset 4.
                var devicePath = Marshal.PtrToStringAuto(detailBuffer + 4);
                if (string.IsNullOrEmpty(devicePath))
                    return false;

                batteryHandle = CreateFile(
                    devicePath,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    nint.Zero,
                    OpenExisting,
                    FileAttributeNormal,
                    nint.Zero);

                if (batteryHandle is null || batteryHandle.IsInvalid)
                    return false;

                // IOCTL_BATTERY_QUERY_TAG -> BatteryTag
                uint batteryTag = 0;
                uint junk = 0;
                if (!DeviceIoControl(
                        batteryHandle,
                        IoctlBatteryQueryTag,
                        ref junk,
                        0,
                        ref batteryTag,
                        sizeof(uint),
                        out _,
                        nint.Zero) || batteryTag == 0)
                {
                    return false;
                }

                var query = new BatteryQueryInformation
                {
                    BatteryTag = batteryTag,
                    InformationLevel = BatteryQueryInformationLevel.BatteryInformation,
                    AtRate = 0,
                };

                var batteryInformation = default(BatteryInformationNative);
                var querySize = Marshal.SizeOf<BatteryQueryInformation>();
                var infoSize = Marshal.SizeOf<BatteryInformationNative>();
                var queryPtr = Marshal.AllocHGlobal(querySize);
                var infoPtr = Marshal.AllocHGlobal(infoSize);
                try
                {
                    Marshal.StructureToPtr(query, queryPtr, false);
                    if (!DeviceIoControl(
                            batteryHandle,
                            IoctlBatteryQueryInformation,
                            queryPtr,
                            (uint)querySize,
                            infoPtr,
                            (uint)infoSize,
                            out _,
                            nint.Zero))
                    {
                        return false;
                    }

                    batteryInformation = Marshal.PtrToStructure<BatteryInformationNative>(infoPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(queryPtr);
                    Marshal.FreeHGlobal(infoPtr);
                }

                var waitStatus = new BatteryWaitStatus
                {
                    BatteryTag = batteryTag,
                    Timeout = 0,
                    PowerState = 0,
                    LowCapacity = 0,
                    HighCapacity = 0,
                };
                var statusSize = Marshal.SizeOf<BatteryStatusNative>();
                var waitSize = Marshal.SizeOf<BatteryWaitStatus>();
                var waitPtr = Marshal.AllocHGlobal(waitSize);
                var statusPtr = Marshal.AllocHGlobal(statusSize);
                try
                {
                    Marshal.StructureToPtr(waitStatus, waitPtr, false);
                    if (!DeviceIoControl(
                            batteryHandle,
                            IoctlBatteryQueryStatus,
                            waitPtr,
                            (uint)waitSize,
                            statusPtr,
                            (uint)statusSize,
                            out _,
                            nint.Zero))
                    {
                        return false;
                    }

                    var status = Marshal.PtrToStructure<BatteryStatusNative>(statusPtr);
                    info = new BatteryRawInfo(
                        batteryInformation.DesignedCapacity,
                        batteryInformation.FullChargedCapacity,
                        (int)status.Capacity,
                        status.Rate,
                        status.Voltage);
                    return info.FullChargeCapacityMwh > 0 || info.CurrentCapacityMwh > 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(waitPtr);
                    Marshal.FreeHGlobal(statusPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            batteryHandle?.Dispose();
            if (!IsInvalidDeviceList(deviceList))
                SetupDiDestroyDeviceInfoList(deviceList);
        }
    }

    private static bool IsInvalidDeviceList(nint handle) =>
        handle == nint.Zero || handle == nint.Zero - 1;

    private static SpDeviceInterfaceData CreateInterfaceData() =>
        new() { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };

    private enum BatteryQueryInformationLevel
    {
        BatteryInformation = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public BatteryQueryInformationLevel InformationLevel;
        public int AtRate;
    }

    // Matches BATTERY_INFORMATION layout from batclass.h / TachiChan.
    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformationNative
    {
        public int Capabilities;
        public byte Technology;
        public byte Reserved0;
        public byte Reserved1;
        public byte Reserved2;
        public byte Chemistry0;
        public byte Chemistry1;
        public byte Chemistry2;
        public byte Chemistry3;
        public int DesignedCapacity;
        public int FullChargedCapacity;
        public int DefaultAlert1;
        public int DefaultAlert2;
        public int CriticalBias;
        public int CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatusNative
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet,
        nint deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        nint deviceInfoData);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref uint lpInBuffer,
        uint nInBufferSize,
        ref uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        nint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}
