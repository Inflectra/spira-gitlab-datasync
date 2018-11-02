using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    /// <summary>
    /// Represents a GitLab issue in their API
    /// </summary>
    public class GitLabIssue
    {
        public GitLabIssue()
        {

        }

        /// <summary>
        /// The name of the Issue in GitLab
        /// </summary>
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        /// <summary>
        /// The GitLab ID of the issue
        /// </summary>
        [JsonProperty("iid", NullValueHandling = NullValueHandling.Ignore)]
        public int IId { get; set; }

        /// <summary>
        /// The body of the issue. Note that it is recieved in Markdown and needs to be converted to HTML
        /// Use CommonMark.CommonMarkConverter.Convert(string) to get HTML
        /// </summary>
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>
        /// The status of the issue, be it open or closed
        /// </summary>
        [JsonProperty("state", NullValueHandling = NullValueHandling.Ignore)]
        public string State { get; set; }

        /// <summary>
        /// The URL to view the issue in GitLab
        /// </summary>
        [JsonProperty("web_url", NullValueHandling = NullValueHandling.Ignore)]
        public string WebUrl { get; set; }

        /// <summary>
        /// The associated URL's
        /// </summary>
        [JsonProperty("_links", NullValueHandling = NullValueHandling.Ignore)]
        public GitLabLinks Links{ get; set; }

        /// <summary>
        /// The creator of the issue
        /// </summary>
        [JsonProperty("author", NullValueHandling = NullValueHandling.Ignore)]
        public GitLabUser creator { get; set; }

        /// <summary>
        /// All of the labels applied to the current issue
        /// </summary>
        /* [JsonProperty("labels", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitLabLabel> labels { get; set; } */

        /// <summary>
        /// All of the users assigned to the issue
        /// </summary>
        [JsonProperty("assignees", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitLabUser> Assignees { get; set; }

        /// <summary>
        /// The time the issue was created
        /// </summary>
        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The time the issue was last updated
        /// </summary>
        [JsonProperty("updated_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// The time the issue was closed, if applicable
        /// </summary>
        [JsonProperty("closed_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? ClosedAt { get; set; }

        /// <summary>
        /// The milestone associated with the issue, if applicable
        /// </summary>
        [JsonProperty("milestone", NullValueHandling = NullValueHandling.Ignore)]
        public GitLabMilestone Milestone { get; set; }

        /// <summary>
        /// All of the comments on the issue. Must be populated manually.
        /// </summary>
        public List<GitLabComment> CommentsList { get; set; }

        public override string ToString()
        {
            return Title;
        }
    }   
}
