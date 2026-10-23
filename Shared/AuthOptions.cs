using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Shared
{
    public class AuthOptions
    {
        public const string ISSUER = "MyBotServer"; // издатель токена
        public const string AUDIENCE = "MyBotClient"; // потребитель токена
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0040:Add accessibility modifiers", Justification = "<Pending>")]
        const string KEY = "mysuperseQWRtaW4rthwrtiLCJJc3N1ZwrthXIiOiJJc3N1ZXIiLCJVc2VybmFwhtZSI6IkphdmFJblVzZSIsImV4cCI6MTY0MDM3MTc0NiwiaWF0IjoxNjQwMzcxNzQ2fQ.n5Fy2TivLw23";   // ключ для шифрации
        public const int LIFETIME = 80000; // время жизни токена - 1 минута
        public static SymmetricSecurityKey GetSymmetricSecurityKey()
        {
            return new SymmetricSecurityKey(Encoding.ASCII.GetBytes(KEY));
        }
    }
}
