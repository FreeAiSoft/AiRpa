using System.Collections.Generic;

namespace Shared.Responses
{
    public class MybotAuthResponse
    {
        public string AccessToken { get; set; }

        public int Code { get; set; }

        public string LastVersion { get; set; }

        public List<int> AppIds { get; set; }

        public int AppId { get; set; }
    }
}
