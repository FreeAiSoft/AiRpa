using System.Runtime.Serialization;

namespace RpaCommon.Enums
{
    public enum IpApiClientStatus
    {
        [EnumMember(Value = "success")]
        Success,
        [EnumMember(Value = "fail")]
        Fail
    }
}
