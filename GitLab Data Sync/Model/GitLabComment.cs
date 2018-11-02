using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    public class GitLabComment
    {

        /// <summary>
        /// The ID of the comment
        /// </summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public long id { get; set; }

        [JsonProperty("created_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime creationDate { get; set; }

        [JsonProperty("updated_at", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime updateDate { get; set; }

        /// <summary>
        /// The body of the comment
        /// </summary>
        [JsonProperty("body", NullValueHandling = NullValueHandling.Ignore)]
        public string body { get; set; }

        /// <summary>
        /// The user who posted the comment
        /// </summary>
        [JsonProperty("author", NullValueHandling = NullValueHandling.Ignore)]
        public GitLabUser commenter { get; set; }

        /// <summary>
        /// Is true when the note should not be posted
        /// </summary>
        [JsonProperty("system", NullValueHandling = NullValueHandling.Ignore)]
        public bool system { get; set; }
    }
}
