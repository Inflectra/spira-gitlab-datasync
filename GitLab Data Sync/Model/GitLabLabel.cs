using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    /// <summary>
    /// A label on a GitLab issue 
    /// see https://help.gitlab.com/articles/about-labels/
    /// </summary>
    public class GitLabLabel
    {
        /// <summary>
        /// The id of the given label type
        /// </summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long labelId { get; set; }

        /// <summary>
        /// The name of the label
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string labelName { get; set; }
    }
}
