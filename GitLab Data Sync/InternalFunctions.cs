using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GitLabDataSync.SpiraSoapService;
using Newtonsoft.Json.Linq;
using System.Net;
using System.IO;
using GitLabDataSync.Model;
using Newtonsoft.Json;
using GitLabDataSync.Model.ToGitLab;
using System.Reflection;

namespace GitLabDataSync
{
    /// <summary>
    /// Contains helper-functions used by the data-sync
    /// </summary>
    public static class InternalFunctions
    {
        /// <summary>
        /// A response from an HTTP request
        /// </summary>
        public struct Response
        {
            public string body;
            public WebHeaderCollection headers;

            public Response(string body, WebHeaderCollection headers)
            {
                this.body = body;
                this.headers = headers;
            }
        }

        /// <summary>
        /// A unique string for Spira denoted by two hard spaces
        /// </summary>
        public const string POSTED_STRING = "Posted By: ";

        /// <summary>
        /// Adds the private_token field to the url
        /// </summary>
        /// <param name="url"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        private static string addToken(string url, string password)
        {
            //is true if there is already a parameter on the url
            bool hasParameter = url.IndexOf('?') != -1;
            if(hasParameter)
            {
                url += "&private_token=" + password;
            }
            else
            {
                url += "?private_token=" + password;
            }
            return url;
        }

        /// <summary>
        /// Forces %2F to be in the path
        /// </summary>
        /// <param name="uri"></param>
        private static void ForceCanonicalPathAndQuery(Uri uri)
        {
            string paq = uri.PathAndQuery; // need to access PathAndQuery
            FieldInfo flagsFieldInfo = typeof(Uri).GetField("m_Flags", BindingFlags.Instance | BindingFlags.NonPublic);
            ulong flags = (ulong)flagsFieldInfo.GetValue(uri);
            flags &= ~((ulong)0x30); // Flags.PathNotCanonical|Flags.QueryNotCanonical
            flagsFieldInfo.SetValue(uri, flags);
        }

        /// <summary>
        /// Performs an HTTP GET request on the given URL from GitLab with the given credentials
        /// Adds the private token parameter
        /// </summary>
        /// <returns>
        /// The body returned by the GET request
        /// </returns>
        public static Response httpGET(string url, string password)
        {
            //add the token
            url = addToken(url, password);

            //GitLab needs %2F to be the divider between the repo and the project not a slash (/)
            //https://stackoverflow.com/questions/4379674/httpwebrequest-url-escaping
            Uri uri = new Uri(url);
            ForceCanonicalPathAndQuery(uri);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(uri) as HttpWebRequest;
            //add neccessary headers
            request.Accept = "application/json";
            request.ContentType = "application/json";
            request.UserAgent = "Spira DataSync";
            request.Method = "GET";

            HttpWebResponse response = null;

            try
            {
                response = request.GetResponse() as HttpWebResponse;
            }
            catch (WebException exception)
            {
                //Add more detail (e.g. the URL) to the exception to help debugging
                throw new WebException(String.Format("Error returned from GitLab API, Method={0}, URL={1}, Response={2}, Message={3}", request.Method, url, exception.Status.ToString(), exception.Message));
            }

            string html = "";
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    html += reader.ReadToEnd();
                }
            }

            //struct with the body and headers
            Response r = new Response(html, response.Headers);

            return r;
        }

        /// <summary>
        /// Perform an HTTP POST request on the given url from GitLab with the given credentials
        /// </summary>
        /// <returns>
        /// The body returned by the POST request
        /// </returns>
        public static string httpPOST(string url, string password, string body)
        {
            //add private token
            url = addToken(url, password);

            //GitLab needs %2F to be the divider between the repo and the project not a slash (/)
            //https://stackoverflow.com/questions/4379674/httpwebrequest-url-escaping
            Uri uri = new Uri(url);
            ForceCanonicalPathAndQuery(uri);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(uri) as HttpWebRequest;
            //add neccessary headers
            request.Accept = "application/json";
            request.UserAgent = "Spira DataSync";
            request.ContentType = "application/json";

            request.Method = "POST";
            //send the data to the server
            using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(body);
            }

            //read the server response
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;

            string html = "";
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    html += reader.ReadToEnd();
                }
            }

            return html;
        }

        public static string httpPATCH(string url, string password, string body)
        {
            //add the private token
            url = addToken(url, password);

            //GitLab needs %2F to be the divider between the repo and the project not a slash (/)
            //https://stackoverflow.com/questions/4379674/httpwebrequest-url-escaping
            Uri uri = new Uri(url);
            ForceCanonicalPathAndQuery(uri);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(uri) as HttpWebRequest;

            request.Accept = "application/json";
            request.UserAgent = "Spira DataSync";
            request.ContentType = "application/json";

            request.Method = "PATCH";
            //send the data to the server
            using (StreamWriter writer = new StreamWriter(request.GetRequestStream()))
            {
                writer.Write(body);
            }

            //read the server response
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;

            string html = "";
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    html += reader.ReadToEnd();
                }
            }

            return html;
        }

        public static string httpPUT(string url, string password)
        {
            //add the private token
            url = addToken(url, password);

            //GitLab needs %2F to be the divider between the repo and the project not a slash (/)
            //https://stackoverflow.com/questions/4379674/httpwebrequest-url-escaping
            Uri uri = new Uri(url);
            ForceCanonicalPathAndQuery(uri);

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            HttpWebRequest request = WebRequest.Create(uri) as HttpWebRequest;

            request.Accept = "application/json";
            request.UserAgent = "Spira DataSync";
            request.ContentType = "application/json";

            request.Method = "PUT";

            //read the server response
            HttpWebResponse response = request.GetResponse() as HttpWebResponse;

            string html = "";
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    html += reader.ReadToEnd();
                }
            }

            return html;
        }

        /// <summary>
        /// Create a new milestone in GitLab from the given parameters
        /// </summary>
        /// <returns>
        /// The new GitLab Milestone
        /// </returns>
        public static GitLabMilestone createMilestone(string externalProject, RemoteRelease remoteRelease, List<GitLabMilestone> milestones, DataSync sync)
        {
            //create a new milestone in GitLab
            NewGitLabMilestone newMilestone = new NewGitLabMilestone();
            newMilestone.title = remoteRelease.Name;
            newMilestone.description = remoteRelease.Description;
            newMilestone.dueDate = remoteRelease.EndDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:MM:ssZ");
            //turn the new milestone into JSON
            string toPost = JsonConvert.SerializeObject(newMilestone);
            //post the new milestone
            //TODO: Milestone, project ID
            string response = httpPOST(sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject  + "/milestones", sync.externalPassword, toPost);
            GitLabMilestone m = JsonConvert.DeserializeObject<GitLabMilestone>(response);

            //add the new milestone to our list
            milestones.Add(m);

            return m;
        }

        /// <summary>
        /// Get the list of milestones in the give project in GitLab
        /// </summary>
        /// <param name="sync"></param>
        /// <returns></returns>
        public static List<GitLabMilestone> getMilestones(string externalProject, DataSync sync)
        {
            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/milestones";
            List<GitLabMilestone> milestones = new List<GitLabMilestone>();

            //need to get multiple pages of milestones
            while (url != null)
            {
                Response response = httpGET(url, sync.externalPassword);
                milestones.AddRange(JsonConvert.DeserializeObject<List<GitLabMilestone>>(response.body));

                url = nextUrl(response.headers);
            }

            return milestones;
        }

        /// <summary>
        /// Sets the status of the issue in GitLab to either open or closed
        /// </summary>
        /// <param name="sync"></param>
        /// <param name="issue"></param>
        public static void setGitLabIssueStatus(string externalProject, DataSync sync, GitLabIssue issue, string status)
        {
            string state = status;
            if(status == "opened")
            {
                state = "reopen";
            }
            if (status == "closed")
            {
                state = "close";
            }

            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues/" + issue.IId + "?state_event=" + state;

            httpPUT(url, sync.externalPassword);
        }

        /// <summary>
        /// Set the milestone of the issue in GitLab
        /// </summary>
        /// <param name="sync"></param>
        /// <param name="issue"></param>
        /// <param name="milestone"></param>
        public static void setGitLabIssueMilestone(string externalProject, DataSync sync, GitLabIssue issue, GitLabMilestone milestone)
        {
            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues/" + issue.IId + "?milestone_id=" + milestone.globalId;

            httpPUT(url, sync.externalPassword);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>
        /// The new GitLab Issue
        /// </returns>
        public static GitLabIssue createIssue(string externalProject, DataSync sync, string externalAssignee, string externalDescription, string externalName, GitLabMilestone milestone)   
        {
            NewGitLabIssue newIssue = new NewGitLabIssue();

            newIssue.Assignees = new List<String>(1);
            newIssue.Assignees.Add(externalAssignee);

            newIssue.Description = CommonMark.CommonMarkConverter.Convert(externalDescription);
            newIssue.Title = externalName;

            if (milestone != null)
            {
                newIssue.Milestone = milestone.milestoneId;
            }
            //TODO: Handle Issue Labels
            string toPost = JsonConvert.SerializeObject(newIssue);
            //post the new issue
            string response = httpPOST(sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues", sync.externalPassword, toPost);

            return JsonConvert.DeserializeObject<GitLabIssue>(response);
        }

        public static List<GitLabIssue> getIssues(string externalProject, DataSync sync, DateTime filterDate)
        {
            //create the URL and perform the GET request
            //Make sure the date-time is UTC
            filterDate = DateTime.SpecifyKind(filterDate, DateTimeKind.Utc);
            string issueUrl = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues?updated_after=" + filterDate.ToString("o") + "&scope=all";

            List<GitLabIssue> externalSystemBugs = new List<GitLabIssue>();

            while (issueUrl != null)
            {
                Response httpResponse = InternalFunctions.httpGET(issueUrl, sync.externalPassword);
                //parse the JSON and get a list from it
                List<GitLabIssue> newIssues = JsonConvert.DeserializeObject<List<GitLabIssue>>(httpResponse.body);
                //add each new issue to the list
                foreach (GitLabIssue issue in newIssues)
                {
                    //get the comments about the specific issue
                    List<GitLabComment> comments = GetGitLabComments(externalProject, sync, issue);
                    issue.CommentsList = comments;
                    externalSystemBugs.Add(issue);
                }
                //get the next url from the link header
                issueUrl = nextUrl(httpResponse.headers);
            }

            return externalSystemBugs;
        }
        /// <summary>
        /// Post a new comment to GitLab
        /// </summary>
        /// <param name="sync"></param>
        /// <param name="id"></param>
        /// <param name="body">In HTML</param>
        public static void postComment(string externalProject, DataSync sync, int id, string body, string username)
        {
            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues/" + id + "/notes";

            NewGitLabComment newComment = new NewGitLabComment();
            //add the posted by field
            newComment.body = addUsername(body, username);
            string toPost = JsonConvert.SerializeObject(newComment);

            string response = httpPOST(url, sync.externalPassword, toPost);
        }

        /// <summary>
        /// Get the specific issue by ID
        /// </summary>
        /// <param name="sync"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static GitLabIssue getSpecificIssue(string externalProject, DataSync sync, int id)
        {
            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues/" + id;
            //perform the GET request
            Response response = httpGET(url, sync.externalPassword);
            GitLabIssue issue = JsonConvert.DeserializeObject<GitLabIssue>(response.body);
            return issue;
        }

        /// <summary>
        /// Get the comments on a specific GitLab issue
        /// </summary>
        /// <param name="issue"></param>
        /// <param name="connectionString"></param>
        /// <param name="externalLogin"></param>
        /// <param name="externalPassword"></param>
        /// <returns></returns>
        public static List<GitLabComment> GetGitLabComments(string externalProject, DataSync sync, GitLabIssue issue)
        {
            List<GitLabComment> comments = new List<GitLabComment>();
            string url = sync.apiUrl + "/projects/" + sync.connectionString + "%2F" + externalProject + "/issues/" + issue.IId + "/notes";

            //go through all of the comments pages
            while (url != null)
            {
                Response httpResponse = httpGET(url, sync.externalPassword);
                List<GitLabComment> newComments = JsonConvert.DeserializeObject<List<GitLabComment>>(httpResponse.body);
                comments.AddRange(newComments);
                url = nextUrl(httpResponse.headers);
            }

            return comments;
        }

        /// <summary>
        /// Add the username of the poster to the top of the comment
        /// </summary>
        /// <param name="comment"></param>
        /// <param name="username"></param>
        /// <returns></returns>
        public static string addUsername(string comment, string username)
        {
            string s = "<b>" + POSTED_STRING + username + "</b> <br/> " + comment;
            return s;
        }

        /// <summary>
        /// Checks if the comment can be posted based on the special "Posted By" string
        /// </summary>
        /// <param name="comment"></param>
        /// <returns></returns>
        public static bool canPostComment(string comment)
        { 
            //don't add the comment if it has the special "Posted By" string
            if(comment.IndexOf(POSTED_STRING) == -1) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Makes sure the comment doesn't already exist
        /// </summary>
        /// <param name="comment"></param>
        /// <param name="comments"></param>
        /// <returns></returns>
        public static bool noCommentExists(string comment, List<GitLabComment> comments)
        {
            comment = CommonMark.CommonMarkConverter.Convert(comment);
            foreach (GitLabComment c in comments)
            {
                
                if(trimComment(c.body).Contains(trimComment(comment)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Makes sure the comment doesn't already exist
        /// </summary>
        /// <param name="comment"></param>
        /// <param name="comments"></param>
        /// <returns></returns>
        public static bool noCommentExists(string comment, RemoteComment[] comments, DataSync sync)
        {
            comment = CommonMark.CommonMarkConverter.Convert(comment);
            foreach(RemoteComment c in comments)
            {
                if(trimComment(c.Text).Contains(trimComment(comment)))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Makes comments comparable be removing excess information
        /// </summary>
        /// <param name="comment"></param>
        /// <returns></returns>
        private static string trimComment(string comment)
        {
            comment = HtmlRenderAsPlainText(comment).Replace("\r", "").Replace("\n", "").Trim();
            return comment;
        }

        /// <summary>
        /// Get the next page from the header, see https://developer.gitlab.com/v3/#pagination
        /// </summary>
        /// <returns>The URL to goto, or null if there is none</returns>
        private static string nextUrl(WebHeaderCollection headers)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers.Keys[i] == "Link")
                {
                    string[] header = headers[i].Split(',');
                    foreach (string s in header)
                    {
                        if (s.Contains("rel=\"next\""))
                        {
                            //extract the actual url from the header
                            int startIndex = s.IndexOf('<') + 1;
                            int length = s.IndexOf('>') - startIndex;
                            string o = s.Substring(startIndex, length);
                            return o;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a mapping entry from the internal id and project id
        /// </summary>
        /// <param name="projectId">The project id</param>
        /// <param name="internalId">The internal id</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        public static SpiraSoapService.RemoteDataMapping FindMappingByInternalId(int projectId, int internalId, SpiraSoapService.RemoteDataMapping[] dataMappings)
        {
            foreach (SpiraSoapService.RemoteDataMapping dataMapping in dataMappings)
            {
                if (dataMapping.InternalId == internalId && dataMapping.ProjectId == projectId)
                {
                    return dataMapping;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a mapping entry from the external key and project id
        /// </summary>
        /// <param name="projectId">The project id</param>
        /// <param name="externalKey">The external key</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <param name="onlyPrimaryEntries">Do we only want to locate primary entries</param>
        /// <returns>The matching entry or Null if none found</returns>
        public static SpiraSoapService.RemoteDataMapping FindMappingByExternalKey(int projectId, string externalKey, SpiraSoapService.RemoteDataMapping[] dataMappings, bool onlyPrimaryEntries)
        {
            foreach (SpiraSoapService.RemoteDataMapping dataMapping in dataMappings)
            {
                if (dataMapping.ExternalKey == externalKey && dataMapping.ProjectId == projectId)
                {
                    //See if we're only meant to return primary entries
                    if (!onlyPrimaryEntries || dataMapping.Primary)
                    {
                        return dataMapping;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a mapping entry from the internal id
        /// </summary>
        /// <param name="internalId">The internal id</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>Used when no project id stored in the mapping collection</remarks>
        public static SpiraSoapService.RemoteDataMapping FindMappingByInternalId(int internalId, SpiraSoapService.RemoteDataMapping[] dataMappings)
        {
            foreach (SpiraSoapService.RemoteDataMapping dataMapping in dataMappings)
            {
                if (dataMapping.InternalId == internalId)
                {
                    return dataMapping;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a mapping entry from the external key
        /// </summary>
        /// <param name="externalKey">The external key</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>Used when no project id stored in the mapping collection</remarks>
        public static SpiraSoapService.RemoteDataMapping FindMappingByExternalKey(string externalKey, SpiraSoapService.RemoteDataMapping[] dataMappings)
        {
            foreach (SpiraSoapService.RemoteDataMapping dataMapping in dataMappings)
            {
                if (dataMapping.ExternalKey == externalKey)
                {
                    return dataMapping;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the GitLabIssue from the list by ID
        /// </summary>
        /// <param name="id"></param>
        /// <param name="issues"></param>
        /// <returns></returns>
        public static GitLabIssue getIssueFromList(int id, List<GitLabIssue> issues)
        {
            foreach(GitLabIssue issue in issues)
            {
                if(issue.IId == id)
                {
                    return issue;
                }
            }
            return null;
        }

        /// <summary>
        /// Sets a custom property value on an artifact, even if it doesn't have an entry yet, can handle the various types
        /// </summary>
        /// <param name="remoteArtifact">The artifact we're setting the properties on</param>
        /// <param name="propertyNumber">The position number (1-30) of the custom property</param>
        /// <param name="propertyValue">The typed property value</param>
        public static void SetCustomPropertyValue<T>(RemoteArtifact remoteArtifact, int propertyNumber, T propertyValue)
        {
            //First see if we have any custom properties at all for this artifact, if not, create a collection
            List<RemoteArtifactCustomProperty> artifactCustomProperties;
            if (remoteArtifact.CustomProperties == null)
            {
                artifactCustomProperties = new List<RemoteArtifactCustomProperty>();
            }
            else
            {
                artifactCustomProperties = remoteArtifact.CustomProperties.ToList();
            }

            //Now see if we have a matching property already in the list
            RemoteArtifactCustomProperty artifactCustomProperty = artifactCustomProperties.FirstOrDefault(c => c.PropertyNumber == propertyNumber);
            if (artifactCustomProperty == null)
            {
                artifactCustomProperty = new RemoteArtifactCustomProperty();
                artifactCustomProperty.PropertyNumber = propertyNumber;
                artifactCustomProperties.Add(artifactCustomProperty);
            }

            //Set the value that matches this type
            if (typeof(T) == typeof(String))
            {
                artifactCustomProperty.StringValue = ((T)propertyValue as String);
            }
            if (typeof(T) == typeof(Int32) || typeof(T) == typeof(Nullable<Int32>))
            {
                artifactCustomProperty.IntegerValue = ((T)propertyValue as Int32?);
            }

            if (typeof(T) == typeof(Boolean) || typeof(T) == typeof(Nullable<Boolean>))
            {
                artifactCustomProperty.BooleanValue = ((T)propertyValue as bool?);
            }

            if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(Nullable<DateTime>))
            {
                artifactCustomProperty.DateTimeValue = ((T)propertyValue as DateTime?);
            }

            if (typeof(T) == typeof(Decimal) || typeof(T) == typeof(Nullable<Decimal>))
            {
                artifactCustomProperty.DecimalValue = ((T)propertyValue as Decimal?);
            }

            if (typeof(T) == typeof(Int32[]))
            {
                artifactCustomProperty.IntegerListValue = ((T)propertyValue as Int32[]);
            }

            if (typeof(T) == typeof(List<Int32>))
            {
                List<Int32> intList = (List<Int32>)((T)propertyValue as List<Int32>);
                if (intList == null || intList.Count == 0)
                {
                    artifactCustomProperty.IntegerListValue = null;
                }
                else
                {
                    artifactCustomProperty.IntegerListValue = intList.ToArray();
                }
            }

            //Finally we need to update the artifact's array
            remoteArtifact.CustomProperties = artifactCustomProperties.ToArray();
        }

        /// <summary>
        /// Gets the deserialized custom property value in a format that can be handled by TFS
        /// </summary>
        /// <param name="artifactCustomProperty">The artifact custom property</param>
        /// <returns></returns>
        /// <remarks>Not to be used for multi-valued list properties</remarks>
        public static object GetCustomPropertyValue(RemoteArtifactCustomProperty artifactCustomProperty)
        {
            //See if we have value on one of the non-string types
            if (artifactCustomProperty.BooleanValue.HasValue)
            {
                return artifactCustomProperty.BooleanValue.Value;
            }
            if (artifactCustomProperty.DateTimeValue.HasValue)
            {
                return artifactCustomProperty.DateTimeValue.Value;
            }
            if (artifactCustomProperty.DecimalValue.HasValue)
            {
                return artifactCustomProperty.DecimalValue.Value;
            }
            if (artifactCustomProperty.IntegerValue.HasValue)
            {
                return artifactCustomProperty.IntegerValue.Value;
            }

            //Otherwise just return the string value
            return artifactCustomProperty.StringValue;
        }

        /// <summary>
        /// Renders HTML content as plain text, since GitLab cannot handle tags
        /// </summary>
        /// <param name="source">The HTML markup</param>
        /// <returns>Plain text representation</returns>
        /// <remarks>Handles line-breaks, etc.</remarks>
        public static string HtmlRenderAsPlainText(string source)
        {
            try
            {
                string result;

                // Remove HTML Development formatting
                // Replace line breaks with space
                // because browsers inserts space
                result = source.Replace("\r", " ");
                // Replace line breaks with space
                // because browsers inserts space
                result = result.Replace("\n", " ");
                // Remove step-formatting
                result = result.Replace("\t", string.Empty);
                // Remove repeating speces becuase browsers ignore them
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"( )+", " ");

                // Remove the header (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*head([^>])*>", "<head>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"(<( )*(/)( )*head( )*>)", "</head>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(<head>).*(</head>)", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // remove all scripts (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*script([^>])*>", "<script>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"(<( )*(/)( )*script( )*>)", "</script>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                //result = System.Text.RegularExpressions.Regex.Replace(result, 
                //         @"(<script>)([^(<script>\.</script>)])*(</script>)",
                //         string.Empty, 
                //         System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"(<script>).*(</script>)", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // remove all styles (prepare first by clearing attributes)
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*style([^>])*>", "<style>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"(<( )*(/)( )*style( )*>)", "</style>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(<style>).*(</style>)", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert tabs in spaces of <td> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*td([^>])*>", "\t",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert line breaks in places of <BR> and <LI> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*br( )*>", "\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*br( )*/>", "\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*li( )*>", "\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // insert line paragraphs (double line breaks) in place
                // if <P>, <DIV> and <TR> tags
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*div([^>])*>", "\r\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*tr([^>])*>", "\r\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<( )*p([^>])*>", "\r\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Remove remaining tags like <a>, links, images,
                // comments etc - anything thats enclosed inside < >
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<[^>]*>", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // replace special characters:
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&nbsp;", " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&bull;", " * ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&lsaquo;", "<",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&rsaquo;", ">",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&trade;", "(tm)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&frasl;", "/",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"<", "<",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @">", ">",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&copy;", "(c)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&reg;", "(r)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove all others. More can be added, see
                // http://hotwired.lycos.com/webmonkey/reference/special_characters/
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    @"&(.{2,6});", string.Empty,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // for testng
                //System.Text.RegularExpressions.Regex.Replace(result, 
                //       this.txtRegex.Text,string.Empty, 
                //       System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // make line breaking consistent
                result = result.Replace("\n", "\r");

                // Remove extra line breaks and tabs:
                // replace over 2 breaks with 2 and over 4 tabs with 4. 
                // Prepare first to remove any whitespaces inbetween
                // the escaped characters and remove redundant tabs inbetween linebreaks
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\r)( )+(\r)", "\r\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\t)( )+(\t)", "\t\t",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\t)( )+(\r)", "\t\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\r)( )+(\t)", "\r\t",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove redundant tabs
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\r)(\t)+(\r)", "\r\r",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove multible tabs followind a linebreak with just one tab
                result = System.Text.RegularExpressions.Regex.Replace(result,
                    "(\r)(\t)+", "\r\t",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Initial replacement target string for linebreaks
                string breaks = "\r\r\r";
                // Initial replacement target string for tabs
                string tabs = "\t\t\t\t\t";
                for (int index = 0; index < result.Length; index++)
                {
                    result = result.Replace(breaks, "\r\r");
                    result = result.Replace(tabs, "\t\t\t\t");
                    breaks = breaks + "\r";
                    tabs = tabs + "\t";
                }

                // Thats it.
                return result;

            }
            catch
            {
                return source;
            }
        }
    }
}
