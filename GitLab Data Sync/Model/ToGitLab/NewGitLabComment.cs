using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model.ToGitLab
{
    /// <summary>
    /// A new comment to be posted to GitLab
    /// </summary>
    public class NewGitLabComment
    {
        [JsonProperty("body", NullValueHandling = NullValueHandling.Ignore)]
        public string body { get; set; }
    }
}
