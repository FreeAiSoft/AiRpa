using System.Runtime.Serialization;

namespace Shared.Enums
{
    public enum IpApiClientStatus
    {
        [EnumMember(Value = "success")]
        Success,
        [EnumMember(Value = "fail")]
        Fail
    }
}
