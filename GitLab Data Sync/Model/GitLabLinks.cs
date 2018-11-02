using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitLabDataSync.Model
{
    /// <summary>
    /// Links provided by GitLab for notes (comments), project, etc
    /// </summary>
    public class GitLabLinks
    {
        /// <summary>
        /// Url to comments (notes)
        /// </summary>
        [JsonProperty("notes", NullValueHandling = NullValueHandling.Ignore)]
        public string notesUrl { get; set; }

        /// <summary>
        /// Url to self
        /// </summary>
        [JsonProperty("self", NullValueHandling = NullValueHandling.Ignore)]
        public string selfUrl { get; set; }
    }
}
