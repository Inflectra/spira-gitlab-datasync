using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    /// <summary>
    /// Represents a user in GitLab
    /// </summary>
    public class GitLabUser
    {
        /// <summary>
        /// The users GitLab username
        /// </summary>
        [JsonProperty("username", NullValueHandling = NullValueHandling.Ignore)]
        public string username { get; set; }

        /// <summary>
        /// The ID of the GitLab user
        /// </summary>
        [JsonProperty("id", NullValueHandling =NullValueHandling.Ignore)]
        public int userId { get; set; }
    }
}
