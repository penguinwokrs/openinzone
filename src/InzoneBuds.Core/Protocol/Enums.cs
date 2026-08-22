namespace InzoneBuds.Protocol;

/// <summary>Identifies which setting or status a packet refers to.</summary>
public enum EventId : byte
{
    ConnectStatus2Ghz = 0x01,
    ModelInfo = 0x02,
    FirmwareVersion = 0x03,
    BatteryInfo = 0x04,
    HostSelectSwitch = 0x05,
    AllFunctionSettingsPart1 = 0x06,
    AllFunctionSettingsPart2 = 0x07,
    AllFunctionSettingsPart3 = 0x08,
    BootStatus = 0x09,

    HeadphoneVolume = 0x21,
    GameChatMixBalance = 0x22,
    SidetoneVolume = 0x23,
    MicVolume = 0x24,
    SurroundSetting = 0x25,

    AmbientSetting = 0x41,
    NoiseCancellingToggle = 0x42,
    NoiseCancellingStartupMode = 0x43,

    BluetoothStatus = 0x61,
    BluetoothSoundQuality = 0x62,
    BluetoothStartupMode = 0x63,

    AutoPowerOff = 0x81,
    LedSetting = 0x82,
    VoicePromptLanguage = 0x83,
    Guidance = 0x84,
    ConnectionDestinationMode = 0x85,
    WearingDetectorCapability = 0x86,
    WearingDetectorStatus = 0x87,
    WearingDetectorParam = 0x88,
    WearingDetectorExtendedParam = 0x89,
    ImpressInfo = 0x8A,
    ImpressData = 0x8B,
    AssignableSettingsCapability = 0x8C,
    AssignableSettingsParam = 0x8D,
    IncomingPermission = 0x8E,
    MicAttachedStatus = 0x8F,
}

[Flags]
public enum EventType : byte
{
    Get = 0x01,
    Set = 0x02,
    Ret = 0x10,
    Notify = 0x20,
    NotifyActive = 0xA0,
}

/// <summary>Endpoints on the link: the PC, the USB transmitter (dongle), and the receiver (earbuds).</summary>
[Flags]
public enum DeviceAddress : byte
{
    Pc = 0x1,
    Transmitter = 0x2,
    Receiver = 0x4,
}

public enum HciPacketType : byte
{
    Command = 0x01,
    Event = 0x04,
}
