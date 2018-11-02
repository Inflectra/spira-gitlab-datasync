using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    /// <summary>
    /// A milestone for an issue in GitLab. Analagous to the release in Spira
    /// </summary>
    public class GitLabMilestone
    {

        /// <summary>
        /// The ID of the milestone
        /// </summary>
        [JsonProperty("iid", NullValueHandling = NullValueHandling.Ignore)]
        public int milestoneId { get; set; }

        /// <summary>
        /// The Global ID of the milestone
        /// </summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long globalId { get; set; }

        /// <summary>
        /// The name of the milestone
        /// </summary>
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string name { get; set; }

        /// <summary>
        /// The description of the milestone
        /// </summary>
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string description { get; set; }

        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime creationDate { get; set; }

        [JsonProperty("due_date", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime dueDate { get; set; }

        /// <summary>
        /// The status of the milestone, be it open or closed
        /// </summary>
        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public string status { get; set; }
    }
}
