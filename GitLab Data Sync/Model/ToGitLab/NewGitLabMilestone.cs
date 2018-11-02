using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model.ToGitLab
{
    /// <summary>
    /// A new milestone to be posted to GitLab
    /// </summary>
    public class NewGitLabMilestone
    {
        /// <summary>
        /// The title of the new Milestone
        /// </summary>
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string title { get; set; }

        /// <summary>
        /// The state, open or closed
        /// </summary>
        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public string state { get; set; }


        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string description { get; set; }

        [JsonProperty("due_on", NullValueHandling = NullValueHandling.Ignore)]
        public string dueDate { get; set; }
    }
}
