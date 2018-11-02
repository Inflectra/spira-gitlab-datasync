using GitLabDataSync.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model.ToGitLab
{
    /// <summary>
    /// A new issue to post to GitLab
    /// </summary>
    class NewGitLabIssue
    {
        /// <summary>
        /// The title (name) of the issue
        /// </summary>
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        /// <summary>
        /// The contents of the issue, in markdown
        /// </summary>
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>
        /// The list of GitLab usernames assigned to the new issue
        /// </summary>
        [JsonProperty("assignees", NullValueHandling = NullValueHandling.Ignore)]
        public List<String> Assignees { get; set; }

        /// <summary>
        /// The ID of the milestone associated with the issue, if applicable
        /// </summary>
        [JsonProperty("milestone", NullValueHandling = NullValueHandling.Ignore)]
        public int? Milestone { get; set; }
    }
}
