using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Inflectra.SpiraTest.PlugIns;
using System.Diagnostics;
using GitLabDataSync.SpiraSoapService;
using System.Globalization;
using System.ServiceModel;
using System.Net;
using Newtonsoft.Json;
using GitLabDataSync.Model;

namespace GitLabDataSync
{
    /// <summary>
    /// Sample data-synchronization provider that synchronizes incidents between SpiraTest/Plan/Team and an external system
    /// </summary>
    /// <remarks>
    /// Requires Spira v4.0 or newer since it uses the v4.0+ compatible web service API
    /// </remarks>
    public class DataSync : IDataSyncPlugIn
    {

        #region Variables
        //Constant containing data-sync name and internal API URL suffix to access
        public const string DATA_SYNC_NAME = "GitLab Data Sync"; //The name of the data-synchronization plugin
        public const string EXTERNAL_SYSTEM_NAME = "GitLab Integrated Issue Tracker";  //The name of the external system we're integrating with
        protected const string GITLAB_CLOUD_BASE_URL = "https://gitlab.com";
        protected const string GITLAB_API_URL = "/api/v4";

        // Track whether Dispose has been called.
        private bool disposed = false;

        //Configuration data passed through from calling service
        private EventLog eventLog;
        private bool traceLogging;
        private int dataSyncSystemId;
        private string webServiceBaseUrl;
        private string internalLogin;
        private string internalPassword;

        /// <summary>
        /// The location of the repository, ex. Inflectra/spira-vscode
        /// </summary>
        public string connectionString;
        public string apiUrl;
        public string externalLogin;
        public string externalPassword;
        private int timeOffsetHours;
        private bool autoMapUsers;

        /// <summary>
        /// All of the milestones associated with the given GitLab repository
        /// </summary>
        private List<GitLabMilestone> milestones;

        #endregion

        /// <summary>
        /// Constructor, does nothing - all setup in the Setup() method instead
        /// </summary>
        public DataSync()
        {
            //Does Nothing - all setup in the Setup() method instead
        }

        /// <summary>
        /// Loads in all the configuration information passed from the calling service
        /// </summary>
        /// <param name="eventLog">Handle to the event log to use</param>
        /// <param name="dataSyncSystemId">The id of the plug-in used when accessing the mapping repository</param>
        /// <param name="webServiceBaseUrl">The base URL of the Spira web service</param>
        /// <param name="internalLogin">The login to Spira</param>
        /// <param name="internalPassword">The password used for the Spira login</param>
        /// <param name="connectionString">The URL for accessing the external system, if no http/https provided, assume standard cloud URL</param>
        /// <param name="externalLogin">The login used for accessing the external syst0em</param>
        /// <param name="externalPassword">The password for the external system</param>
        /// <param name="timeOffsetHours">Any time offset to apply between Spira and the external system</param>
        /// <param name="autoMapUsers">Should we auto-map users</param>
        /// <param name="custom01">Custom configuration 01</param>
        /// <param name="custom02">Custom configuration 02</param>
        /// <param name="custom03">Custom configuration 03</param>
        /// <param name="custom04">Custom configuration 04</param>
        /// <param name="custom05">Custom configuration 05</param>
        public void Setup(
            EventLog eventLog,
            bool traceLogging,
            int dataSyncSystemId,
            string webServiceBaseUrl,
            string internalLogin,
            string internalPassword,
            string connectionString,
            string externalLogin,
            string externalPassword,
            int timeOffsetHours,
            bool autoMapUsers,
            string custom01,
            string custom02,
            string custom03,
            string custom04,
            string custom05
            )
        {
            //Make sure the object has not been already disposed
            if (this.disposed)
            {
                throw new ObjectDisposedException(DATA_SYNC_NAME + " has been disposed already.");
            }

            try
            {
                //Set the member variables from the passed-in values
                this.eventLog = eventLog;
                this.traceLogging = traceLogging;
                this.dataSyncSystemId = dataSyncSystemId;
                this.webServiceBaseUrl = webServiceBaseUrl;
                this.internalLogin = internalLogin;
                this.internalPassword = internalPassword;

                //The API URL is normally just the standard GitLab one, unless an override (on-premise one) specified in custom 01
                if (String.IsNullOrEmpty(custom01))
                {
                    this.apiUrl = GITLAB_CLOUD_BASE_URL + GITLAB_API_URL;
                }
                else
                {
                    this.apiUrl = custom01 + GITLAB_API_URL;
                }

                //need to format connector for use with GitLab. See - https://docs.gitlab.com/ee/api/#namespaced-path-encoding
                this.connectionString = connectionString.Replace("/", "%2F");
                this.externalLogin = externalLogin;
                this.externalPassword = externalPassword;
                this.timeOffsetHours = timeOffsetHours;
                this.autoMapUsers = autoMapUsers;
            }
            catch (Exception exception)
            {
                //Log and rethrow the exception
                eventLog.WriteEntry("Unable to setup the " + DATA_SYNC_NAME + " plug-in ('" + exception.Message + "')\n" + exception.StackTrace, EventLogEntryType.Error);
                throw exception;
            }
        }

        /// <summary>
        /// Executes the data-sync functionality between the two systems
        /// </summary>
        /// <param name="LastSyncDate">The last date/time the plug-in was successfully executed (in UTC)</param>
        /// <param name="serverDateTime">The current date/time on the server (in UTC)</param>
        /// <returns>Code denoting success, failure or warning</returns>
        public ServiceReturnType Execute(DateTime? lastSyncDate, DateTime serverDateTime)
        {
            //Make sure the object has not been already disposed
            if (this.disposed)
            {
                throw new ObjectDisposedException(DATA_SYNC_NAME + " has been disposed already.");
            }

            try
            {
                LogTraceEvent(eventLog, "Starting " + DATA_SYNC_NAME + " data synchronization", EventLogEntryType.Information);

                //Instantiate the SpiraTest web-service proxy class
                Uri spiraUri = new Uri(this.webServiceBaseUrl + Constants.WEB_SERVICE_URL_SUFFIX);
                SoapServiceClient spiraSoapService = SpiraClientFactory.CreateClient(spiraUri);                

                //Now lets get the product name we should be referring to
                string productName = spiraSoapService.System_GetProductName();

                //**** Next lets load in the project and user mappings ****
                bool success = spiraSoapService.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                if (!success)
                {
                    //We can't authenticate so end
                    LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                    return ServiceReturnType.Error;
                }
                RemoteDataMapping[] projectMappings = spiraSoapService.DataMapping_RetrieveProjectMappings(dataSyncSystemId);
                RemoteDataMapping[] userMappings = spiraSoapService.DataMapping_RetrieveUserMappings(dataSyncSystemId);

                //Loop for each of the projects in the project mapping
                foreach (RemoteDataMapping projectMapping in projectMappings)
                {
                    //Get the SpiraTest project id equivalent external system project identifier
                    int projectId = projectMapping.InternalId;
                    string externalProjectId = projectMapping.ExternalKey;

                    //verify we can connect to GitLab AND get the list of milestones from GitLab for this project
                    milestones = InternalFunctions.getMilestones(externalProjectId, this);

                    //if we can't connect to GitLab
                    if (milestones == null)
                    {
                        //We can't connect to GitLab
                        LogErrorEvent("Unable to authenticate with GitLab API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //Connect to the SpiraTest project
                    success = spiraSoapService.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent(String.Format("Unable to connect to {0} project PR{1}, please check that the {0} login has the appropriate permissions", productName, projectId), EventLogEntryType.Error);
                        continue;
                    }

                    //Get the list of project-specific mappings from the data-mapping repository
                    //We need to get severity, priority, status and type mappings
                    RemoteDataMapping[] severityMappings = spiraSoapService.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Severity);
                    RemoteDataMapping[] priorityMappings = spiraSoapService.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Priority);
                    RemoteDataMapping[] statusMappings = spiraSoapService.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Status);
                    RemoteDataMapping[] typeMappings = spiraSoapService.DataMapping_RetrieveFieldValueMappings(dataSyncSystemId, (int)Constants.ArtifactField.Type);

                    //Get the list of custom properties configured for this project and the corresponding data mappings
                    RemoteCustomProperty[] incidentCustomProperties = spiraSoapService.CustomProperty_RetrieveForArtifactType((int)Constants.ArtifactType.Incident, false);
                    Dictionary<int, RemoteDataMapping> customPropertyMappingList = new Dictionary<int, RemoteDataMapping>();
                    Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList = new Dictionary<int, RemoteDataMapping[]>();
                    foreach (RemoteCustomProperty customProperty in incidentCustomProperties)
                    {
                        //Get the mapping for this custom property
                        if (customProperty.CustomPropertyId.HasValue)
                        {
                            RemoteDataMapping customPropertyMapping = spiraSoapService.DataMapping_RetrieveCustomPropertyMapping(dataSyncSystemId, (int)Constants.ArtifactType.Incident, customProperty.CustomPropertyId.Value);
                            customPropertyMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyMapping);

                            //For list types need to also get the property value mappings
                            if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List || customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                            {
                                RemoteDataMapping[] customPropertyValueMappings = spiraSoapService.DataMapping_RetrieveCustomPropertyValueMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, customProperty.CustomPropertyId.Value);
                                customPropertyValueMappingList.Add(customProperty.CustomPropertyId.Value, customPropertyValueMappings);
                            }
                        }
                    }

                    //Now get the list of releases and incidents that have already been mapped
                    RemoteDataMapping[] incidentMappings = spiraSoapService.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident);
                    RemoteDataMapping[] releaseMappings = spiraSoapService.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release);

                    //**** First we need to get the list of recently created incidents in SpiraTest ****

                    //If we don't have a last-sync data, default to 1/1/1950
                    if (!lastSyncDate.HasValue)
                    {
                        lastSyncDate = DateTime.ParseExact("1/1/1950", "M/d/yyyy", CultureInfo.InvariantCulture);
                    }

                    DateTime filterDate = lastSyncDate.Value.AddHours(-timeOffsetHours);
                    if (filterDate < DateTime.Parse("1/1/1990"))
                    {
                        filterDate = DateTime.Parse("1/1/1990");
                    }

                    //filter starting at the filterDate
                    RemoteFilter filter = new RemoteFilter();
                    SpiraSoapService.DateRange dateRange = new SpiraSoapService.DateRange();
                    dateRange.StartDate = filterDate.ToUniversalTime();
                    dateRange.EndDate = null;
                    dateRange.ConsiderTimes = true;

                    filter.DateRangeValue = dateRange;

                    RemoteSort sort = new RemoteSort();
                    sort.SortAscending = true;

                    //Get the incidents in batches of 100
                    List<RemoteIncident> incidentList = new List<RemoteIncident>();
                    long incidentCount = spiraSoapService.Incident_Count(null);
                    for (int startRow = 1; startRow <= incidentCount; startRow += Constants.INCIDENT_PAGE_SIZE)
                    {
                        RemoteIncident[] incidentBatch = spiraSoapService.Incident_Retrieve(new RemoteFilter[] { filter }, sort, startRow, Constants.INCIDENT_PAGE_SIZE);
                        incidentList.AddRange(incidentBatch);
                    }
                    LogTraceEvent(eventLog, "Found " + incidentList.Count + " new incidents in " + productName, EventLogEntryType.Information);

                    //get the list of issues from GitLab
                    List<GitLabIssue> externalSystemBugs = InternalFunctions.getIssues(externalProjectId, this, filterDate);


                    //Create the mapping collections to hold any new items that need to get added to the mappings
                    //or any old items that need to get removed from the mappings
                    List<RemoteDataMapping> newIncidentMappings = new List<RemoteDataMapping>();
                    List<RemoteDataMapping> newReleaseMappings = new List<RemoteDataMapping>();
                    List<RemoteDataMapping> oldReleaseMappings = new List<RemoteDataMapping>();
                    RemoteRelease[] releases = spiraSoapService.Release_Retrieve(false);

                    //Iterate through each new Spira incident record and add to the external system
                    foreach (RemoteIncident remoteIncident in incidentList)
                    {
                        try
                        {
                            ProcessIncident(projectId, spiraSoapService, remoteIncident, newIncidentMappings, newReleaseMappings, oldReleaseMappings, customPropertyMappingList, customPropertyValueMappingList, incidentCustomProperties, incidentMappings, externalProjectId, productName, severityMappings, priorityMappings, statusMappings, typeMappings, userMappings, releaseMappings, releases, externalSystemBugs, filterDate);
                        }
                        catch (Exception exception)
                        {
                            //Log and continue execution
                            LogErrorEvent("Error Adding " + productName + " Incident to " + EXTERNAL_SYSTEM_NAME + ": " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                        }
                    }

                    //Finally we need to update the mapping data on the server before starting the second phase
                    //of the data-synchronization
                    //At this point we have potentially added incidents, added releases and removed releases
                    spiraSoapService.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, newIncidentMappings.ToArray());
                    spiraSoapService.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, newReleaseMappings.ToArray());
                    spiraSoapService.DataMapping_RemoveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, oldReleaseMappings.ToArray());

                    //Re-authenticate with Spira and reconnect to the project to avoid potential timeout issues
                    success = spiraSoapService.Connection_Authenticate2(internalLogin, internalPassword, DATA_SYNC_NAME);
                    if (!success)
                    {
                        //We can't authenticate so end
                        LogErrorEvent("Unable to authenticate with " + productName + " API, stopping data-synchronization", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }
                    success = spiraSoapService.Connection_ConnectToProject(projectId);
                    if (!success)
                    {
                        //We can't connect so go to next project
                        LogErrorEvent("Unable to connect to " + productName + " project PR" + projectId + ", please check that the " + productName + " login has the appropriate permissions", EventLogEntryType.Error);
                        return ServiceReturnType.Error;
                    }

                    //**** Next we need to see if any of the previously mapped incidents has changed or any new items added to the external system ****
                    incidentMappings = spiraSoapService.DataMapping_RetrieveArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident);

                    //Need to create a list to hold any new releases and new incidents
                    newIncidentMappings = new List<RemoteDataMapping>();
                    newReleaseMappings = new List<RemoteDataMapping>();

                    //need to fetch the GitLab bugs again to realize any changes
                    externalSystemBugs = InternalFunctions.getIssues(externalProjectId, this, filterDate);

                    //Iterate through these items
                    foreach (GitLabIssue externalSystemBug in externalSystemBugs)
                    {
                        try
                        {
                            //Extract the data from the external bug object and load into Spira as a new incident
                            ProcessExternalBug(projectId, spiraSoapService, externalSystemBug, newIncidentMappings, newReleaseMappings, oldReleaseMappings, customPropertyMappingList, customPropertyValueMappingList, incidentCustomProperties, incidentMappings, externalProjectId, productName, severityMappings, priorityMappings, statusMappings, typeMappings, userMappings, releaseMappings);
                        }
                        catch (Exception exception)
                        {
                            //Log and continue execution
                            LogErrorEvent("Error Inserting/Updating " + EXTERNAL_SYSTEM_NAME + " Bug in " + productName + ": " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                        }
                    }

                    //Finally we need to update the mapping data on the server
                    //At this point we have potentially added releases and incidents
                    spiraSoapService.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Release, newReleaseMappings.ToArray());
                    spiraSoapService.DataMapping_AddArtifactMappings(dataSyncSystemId, (int)Constants.ArtifactType.Incident, newIncidentMappings.ToArray());
                }

                //The following code is only needed during debugging
                LogTraceEvent(eventLog, "Import Completed", EventLogEntryType.Warning);

                //Mark objects ready for garbage collection
                spiraSoapService = null;

                //Let the service know that we ran correctly
                return ServiceReturnType.Success;
            }
            catch (Exception exception)
            {
                //Log the exception and return as a failure
                LogErrorEvent("General Error: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                return ServiceReturnType.Error;
            }
        }

        /// <summary>
        /// Processes a single SpiraTest incident record and adds to the external system
        /// </summary>
        /// <param name="projectId">The id of the current project</param>
        /// <param name="spiraSoapService">The Spira API proxy class</param>
        /// <param name="remoteIncident">The Spira incident</param>
        /// <param name="newIncidentMappings">The list of any new incidents to be mapped</param>
        /// <param name="newReleaseMappings">The list of any new releases to be mapped</param>
        /// <param name="oldReleaseMappings">The list list of old releases to be un-mapped</param>
        /// <param name="customPropertyMappingList">The mapping of custom properties</param>
        /// <param name="customPropertyValueMappingList">The mapping of custom property list values</param>
        /// <param name="incidentCustomProperties">The list of incident custom properties defined for this project</param>
        /// <param name="incidentMappings">The list of existing mapped incidents</param>
        /// <param name="externalProjectId">The id of the project in the external system</param>
        /// <param name="productName">The name of the product being connected to (SpiraTest, SpiraPlan, etc.)</param>
        /// <param name="severityMappings">The incident severity mappings</param>
        /// <param name="priorityMappings">The incident priority mappings</param>
        /// <param name="statusMappings">The incident status mappings</param>
        /// <param name="typeMappings">The incident type mappings</param>
        /// <param name="userMappings">The incident user mappings</param>
        /// <param name="releaseMappings">The release mappings</param>
        private void ProcessIncident(int projectId, SoapServiceClient spiraSoapService, RemoteIncident remoteIncident, List<RemoteDataMapping> newIncidentMappings, List<RemoteDataMapping> newReleaseMappings, List<RemoteDataMapping> oldReleaseMappings, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteCustomProperty[] incidentCustomProperties, RemoteDataMapping[] incidentMappings, string externalProjectId, string productName, RemoteDataMapping[] severityMappings, RemoteDataMapping[] priorityMappings, RemoteDataMapping[] statusMappings, RemoteDataMapping[] typeMappings, RemoteDataMapping[] userMappings, RemoteDataMapping[] releaseMappings, RemoteRelease[] internalReleases, List<GitLabIssue> gitLabIssues, DateTime filterDate)
        {
            //Get certain incident fields into local variables (if used more than once)
            int incidentId = remoteIncident.IncidentId.Value;
            int incidentStatusId = remoteIncident.IncidentStatusId.Value;

            #region create new issue
            //Make sure we've not already loaded this issue
            if (InternalFunctions.FindMappingByInternalId(projectId, incidentId, incidentMappings) == null)
            {
                //Get the URL for the incident in Spira, we'll use it later
                string baseUrl = spiraSoapService.System_GetWebServerUrl();
                string incidentUrl = spiraSoapService.System_GetArtifactUrl((int)Constants.ArtifactType.Incident, projectId, incidentId, "").Replace("~", baseUrl);

                //Get the name/description of the incident. The description will be available in both rich (HTML) and plain-text
                //depending on what the external system can handle
                string externalName = remoteIncident.Name;
                string externalDescriptionHtml = remoteIncident.Description;

                //See if this incident has any associations
                RemoteSort associationSort = new RemoteSort();
                associationSort.SortAscending = true;
                associationSort.PropertyName = "CreationDate";
                RemoteAssociation[] remoteAssociations = spiraSoapService.Association_RetrieveForArtifact((int)Constants.ArtifactType.Incident, incidentId, null, associationSort);

                //See if this incident has any attachments
                RemoteSort attachmentSort = new RemoteSort();
                attachmentSort.SortAscending = true;
                attachmentSort.PropertyName = "AttachmentId";
                RemoteDocument[] remoteDocuments = spiraSoapService.Document_RetrieveForArtifact((int)Constants.ArtifactType.Incident, incidentId, null, attachmentSort);

                //Get some of the incident's non-mappable fields
                DateTime creationDate = remoteIncident.CreationDate.Value;
                DateTime lastUpdateDate = remoteIncident.LastUpdateDate;
                DateTime? startDate = remoteIncident.StartDate;
                DateTime? closedDate = remoteIncident.ClosedDate;
                int? estimatedEffortInMinutes = remoteIncident.EstimatedEffort;
                int? actualEffortInMinutes = remoteIncident.ActualEffort;
                int? projectedEffortInMinutes = remoteIncident.ProjectedEffort;
                int? remainingEffortInMinutes = remoteIncident.RemainingEffort;
                int completionPercent = remoteIncident.CompletionPercent;

                //Now get the external system's equivalent incident status from the mapping
                RemoteDataMapping dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentStatusId.Value, statusMappings);
                if (dataMapping == null)
                {
                    //We can't find the matching item so log and move to the next incident
                    LogErrorEvent("Unable to locate mapping entry for incident status " + remoteIncident.IncidentStatusId + " in project PR" + projectId, EventLogEntryType.Error);
                    return;
                }
                string externalStatus = dataMapping.ExternalKey;

                ////Now get the external system's equivalent incident type from the mapping
                //dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentTypeId.Value, typeMappings);
                //if (dataMapping == null)
                //{
                //    //We can't find the matching item so log and move to the next incident
                //    LogErrorEvent("Unable to locate mapping entry for incident type " + remoteIncident.IncidentTypeId + " in project PR" + projectId, EventLogEntryType.Error);
                //    return;
                //}
                //string externalType = dataMapping.ExternalKey;

                //Now get the external system's equivalent priority from the mapping (if priority is set)
                /* string externalPriority = "";
                if (remoteIncident.PriorityId.HasValue)
                {
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.PriorityId.Value, priorityMappings);
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so log and just don't set the priority
                        LogErrorEvent("Unable to locate mapping entry for incident priority " + remoteIncident.PriorityId.Value + " in project PR" + projectId, EventLogEntryType.Warning);
                    }
                    else
                    {
                        externalPriority = dataMapping.ExternalKey;
                    }
                } */

                //Now get the external system's equivalent severity from the mapping (if severity is set)
                /* string externalSeverity = "";
                if (remoteIncident.SeverityId.HasValue)
                {
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.SeverityId.Value, severityMappings);
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so log and just don't set the severity
                        LogErrorEvent("Unable to locate mapping entry for incident severity " + remoteIncident.SeverityId.Value + " in project PR" + projectId, EventLogEntryType.Warning);
                    }
                    else
                    {
                        externalSeverity = dataMapping.ExternalKey;
                    }
                } */

                ////Now get the external system's ID for the Opener/Detector of the incident (reporter)
                //string externalReporter = "";
                //dataMapping = FindUserMappingByInternalId(remoteIncident.OpenerId.Value, userMappings, spiraSoapService);
                ////If we can't find the user, just log a warning
                //if (dataMapping == null)
                //{
                //    LogErrorEvent("Unable to locate mapping entry for user id " + remoteIncident.OpenerId.Value + " so using synchronization user", EventLogEntryType.Warning);
                //}
                //else
                //{
                //    externalReporter = dataMapping.ExternalKey;
                //}

                //Now get the external system's ID for the Owner of the incident (assignee)
                string externalAssignee = "";
                if (remoteIncident.OwnerId.HasValue)
                {
                    dataMapping = FindUserMappingByInternalId(remoteIncident.OwnerId.Value, userMappings, spiraSoapService);
                    //If we can't find the user, just log a warning
                    if (dataMapping == null)
                    {
                        LogErrorEvent("Unable to locate mapping entry for user id " + remoteIncident.OwnerId.Value + " so leaving assignee empty", EventLogEntryType.Warning);
                    }
                    else
                    {
                        externalAssignee = dataMapping.ExternalKey;
                    }
                }
                GitLabMilestone issueMilestone = null;

                //Specify the resolved-in version/release if applicable
                int externalResolvedRelease = -1;
                if (remoteIncident.ResolvedReleaseId.HasValue)
                {
                    int resolvedReleaseId = remoteIncident.ResolvedReleaseId.Value;
                    dataMapping = InternalFunctions.FindMappingByInternalId(projectId, resolvedReleaseId, releaseMappings);
                    if (dataMapping == null)
                    {
                        //We can't find the matching item so need to create a new version in the external system and add to mappings
                        //Since version numbers are now unique in both systems, we can simply use that
                        LogTraceEvent(eventLog, "Adding new release in " + EXTERNAL_SYSTEM_NAME + " for release " + resolvedReleaseId + "\n", EventLogEntryType.Information);

                        //Get the Spira release
                        RemoteRelease remoteRelease = spiraSoapService.Release_RetrieveById(resolvedReleaseId);
                        if (remoteRelease != null)
                        {
                            GitLabMilestone newMilestone = InternalFunctions.createMilestone(externalProjectId, remoteRelease, milestones, this);
                            externalResolvedRelease = newMilestone.milestoneId;
                            milestones.Add(newMilestone);

                            //Add a new mapping entry
                            RemoteDataMapping newReleaseMapping = new RemoteDataMapping();
                            newReleaseMapping.ProjectId = projectId;
                            newReleaseMapping.InternalId = resolvedReleaseId;
                            newReleaseMapping.ExternalKey = externalResolvedRelease + "";
                            newReleaseMappings.Add(newReleaseMapping);
                        }
                    }
                    else
                    {
                        externalResolvedRelease = Int32.Parse(dataMapping.ExternalKey);
                    }

                    //Verify that this release still exists in the external system
                    LogTraceEvent(eventLog, "Looking for " + EXTERNAL_SYSTEM_NAME + " resolved release: " + externalResolvedRelease + "\n", EventLogEntryType.Information);

                    //find the release in our list of milestones

                    bool matchFound = false;
                    if (externalResolvedRelease != -1)
                    {
                        //look through all of the GitLab milestones
                        foreach (GitLabMilestone m in milestones)
                        {
                            //if we found the release...
                            if (m.milestoneId == externalResolvedRelease)
                            {
                                matchFound = true;
                                issueMilestone = m;
                                break;
                            }
                        }
                    }


                    if (!matchFound)
                    {
                        //We can't find the matching item so log and just don't set the release
                        LogErrorEvent("Unable to locate " + EXTERNAL_SYSTEM_NAME + " resolved release " + externalResolvedRelease + " in project " + externalProjectId, EventLogEntryType.Warning);

                        //Add this to the list of mappings to remove
                        RemoteDataMapping oldReleaseMapping = new RemoteDataMapping();
                        oldReleaseMapping.ProjectId = projectId;
                        oldReleaseMapping.InternalId = resolvedReleaseId;
                        oldReleaseMapping.ExternalKey = externalResolvedRelease + "";
                        oldReleaseMappings.Add(oldReleaseMapping);
                    }
                }
                LogTraceEvent(eventLog, "Set " + EXTERNAL_SYSTEM_NAME + " resolved release\n", EventLogEntryType.Information);

                //Setup the dictionary to hold the various custom properties to set on the external bug system
                //TODO: Replace with the real custom property collection for the external system
                Dictionary<string, object> externalSystemCustomFieldValues = new Dictionary<string, object>();

                //Now we need to see if any of the custom properties have changed
                if (remoteIncident.CustomProperties != null && remoteIncident.CustomProperties.Length > 0)
                {
                    ProcessCustomProperties(productName, projectId, remoteIncident, externalSystemCustomFieldValues, customPropertyMappingList, customPropertyValueMappingList, userMappings, spiraSoapService);
                }
                LogTraceEvent(eventLog, "Captured incident custom values\n", EventLogEntryType.Information);

                GitLabIssue newIssue = InternalFunctions.createIssue(externalProjectId, this, externalAssignee, externalDescriptionHtml, externalName, issueMilestone);
                if (externalStatus == "closed")
                {
                    try
                    {
                        InternalFunctions.setGitLabIssueStatus(externalProjectId, this, newIssue, "closed");
                        newIssue.State = "closed";
                    }
                    catch (Exception exception)
                    {
                        //Log and continue
                        LogErrorEvent(String.Format("Unable to change {0} bug to state {1}, due to error message '{2}'", EXTERNAL_SYSTEM_NAME, "closed", exception), EventLogEntryType.Error);
                    }
                }

                string externalBugId = newIssue.IId + "";

                //Add the external bug id to mappings table
                RemoteDataMapping newIncidentMapping = new RemoteDataMapping();
                newIncidentMapping.ProjectId = projectId;
                newIncidentMapping.InternalId = incidentId;
                newIncidentMapping.ExternalKey = externalBugId;
                newIncidentMappings.Add(newIncidentMapping);

                //We also add a link to the external issue from the Spira incident
                if (!String.IsNullOrEmpty(newIssue.WebUrl))
                {
                    string externalUrl = newIssue.WebUrl;
                    List<RemoteLinkedArtifact> linkedArtifacts = new List<RemoteLinkedArtifact>();
                    linkedArtifacts.Add(new RemoteLinkedArtifact() { ArtifactId = incidentId, ArtifactTypeId = (int)Constants.ArtifactType.Incident });
                    RemoteDocument remoteUrl = new RemoteDocument();
                    remoteUrl.AttachedArtifacts = linkedArtifacts.ToArray();
                    remoteUrl.Description = "Link to issue in " + EXTERNAL_SYSTEM_NAME;
                    remoteUrl.FilenameOrUrl = externalUrl;
                    spiraSoapService.Document_AddUrl(remoteUrl);
                }

                //See if we have any comments to add to the external system
                RemoteComment[] incidentComments = spiraSoapService.Incident_RetrieveComments(incidentId);
                if (incidentComments != null)
                {
                    foreach (RemoteComment incidentComment in incidentComments)
                    {
                        string externalResolutionText = incidentComment.Text;
                        creationDate = incidentComment.CreationDate.Value;

                        //Get the id of the corresponding external user that added the comments
                        string externalCommentAuthor = "";
                        dataMapping = InternalFunctions.FindMappingByInternalId(incidentComment.UserId.Value, userMappings);
                        //If we can't find the user, just log a warning
                        if (dataMapping == null)
                        {
                            LogErrorEvent("Unable to locate mapping entry for user id " + incidentComment.UserId.Value + " so using synchronization user", EventLogEntryType.Warning);
                        }
                        else
                        {
                            externalCommentAuthor = dataMapping.ExternalKey;
                        }

                        List<GitLabComment> externalComments = InternalFunctions.GetGitLabComments(externalProjectId, this, newIssue);

                        //only post if there isn't a comment already
                        if (InternalFunctions.canPostComment(externalResolutionText) && InternalFunctions.noCommentExists(externalResolutionText, externalComments))
                        {
                            InternalFunctions.postComment(externalProjectId, this, Int32.Parse(externalBugId), externalResolutionText, incidentComment.UserName);
                        }
                    }
                }

                //See if we have any attachments to add to the external bug
                /* if (remoteDocuments != null && remoteDocuments.Length > 0)
                {
                    foreach (RemoteDocument remoteDocument in remoteDocuments)
                    {
                        //See if we have a file attachment or simple URL
                        if (remoteDocument.AttachmentTypeId == (int)Constants.AttachmentType.File)
                        {
                            try
                            {
                                //Get the binary data for the attachment
                                byte[] binaryData = spiraSoapService.Document_OpenFile(remoteDocument.AttachmentId.Value);
                                if (binaryData != null && binaryData.Length > 0)
                                {
                                    //TODO: LATER Add the code to add this attachment to the external system
                                    string filename = remoteDocument.FilenameOrUrl;
                                    string description = remoteDocument.Description;
                                }
                            }
                            catch (Exception exception)
                            {
                                //Log an error and continue because this can fail if the files are too large
                                LogErrorEvent("Error adding " + productName + " incident attachment DC" + remoteDocument.AttachmentId.Value + " to " + EXTERNAL_SYSTEM_NAME + ": " + exception.Message + "\n. (The issue itself was added.)\n Stack Trace: " + exception.StackTrace, EventLogEntryType.Error);
                            }
                        }
                        if (remoteDocument.AttachmentTypeId == (int)Constants.AttachmentType.URL)
                        {
                            try
                            {
                                //TODO: LATER Add the code to add this hyperlink to the external system
                                string url = remoteDocument.FilenameOrUrl;
                                string description = remoteDocument.Description;
                            }
                            catch (Exception exception)
                            {
                                //Log an error and continue because this can fail if the files are too large
                                LogErrorEvent("Error adding " + productName + " incident attachment DC" + remoteDocument.AttachmentId.Value + " to " + EXTERNAL_SYSTEM_NAME + ": " + exception.Message + "\n. (The issue itself was added.)\n Stack Trace: " + exception.StackTrace, EventLogEntryType.Error);
                            }
                        }
                    }
                }

                //See if we have any incident-to-incident associations to add to the external bug
                 if (remoteAssociations != null && remoteAssociations.Length > 0)
                {
                    foreach (RemoteAssociation remoteAssociation in remoteAssociations)
                    {
                        //Make sure the linked-to item is an incident
                        if (remoteAssociation.DestArtifactTypeId == (int)Constants.ArtifactType.Incident)
                        {
                            dataMapping = InternalFunctions.FindMappingByInternalId(remoteAssociation.DestArtifactId, incidentMappings);
                            if (dataMapping != null)
                            {
                                //TODO: LATER, MAYBE Add a link in the external system to the following target bug id
                                string externalTargetBugId = dataMapping.ExternalKey;
                            }
                        }
                    }
                } */
            }
            #endregion

            #region update the issue
            else
            {
                //get the ID of the GitLab issue
                int externalId = Int32.Parse(InternalFunctions.FindMappingByInternalId(projectId, incidentId, incidentMappings).ExternalKey);
                //process new comments
                GitLabIssue externalIssue = InternalFunctions.getSpecificIssue(externalProjectId, this, externalId);

                //get the comments
                RemoteComment[] comments = spiraSoapService.Incident_RetrieveComments(remoteIncident.IncidentId.Value);
                List<GitLabComment> gitLabComments = InternalFunctions.GetGitLabComments(externalProjectId, this, externalIssue);

                foreach (RemoteComment comment in comments)
                {
                    //only add the comment if it is new
                    if (comment.CreationDate > filterDate && InternalFunctions.canPostComment(comment.Text) && InternalFunctions.noCommentExists(comment.Text, gitLabComments))
                    {
                        InternalFunctions.postComment(externalProjectId, this, externalId, comment.Text, comment.UserName);
                    }
                }

                if (remoteIncident.LastUpdateDate > externalIssue.UpdatedAt)
                {
                    //get the mapping to the incident status
                    RemoteDataMapping dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentStatusId.Value, statusMappings);

                    //only change the status if the external system needs to be synced
                    if(dataMapping.ExternalKey != externalIssue.State)
                    {
                        try
                        {
                            InternalFunctions.setGitLabIssueStatus(externalProjectId, this, externalIssue, dataMapping.ExternalKey);
                        }
                        catch (Exception exception)
                        {
                            //Log and continue
                            LogErrorEvent(String.Format("Unable to change {0} bug to state {1}, due to error message '{2}'", EXTERNAL_SYSTEM_NAME, externalIssue.State, exception), EventLogEntryType.Error);
                        }
                    }

                    RemoteRelease resolvedRelease = null;
                    //find the resolved release
                    foreach (RemoteRelease release in internalReleases)
                    {
                        if (release.ReleaseId == remoteIncident.ResolvedReleaseId)
                        {
                            resolvedRelease = release;
                            break;
                        }
                    }

                    if (resolvedRelease != null)
                    {
                        GitLabMilestone milestone = null;
                        foreach (GitLabMilestone m in milestones)
                        {
                            if (resolvedRelease.Name == m.name)
                            {
                                milestone = m;
                                break;
                            }
                        }

                        if (milestone == null)
                        {
                            milestone = InternalFunctions.createMilestone(externalProjectId, resolvedRelease, milestones, this);
                            milestones.Add(milestone);
                        }

                        //set the release of the GitLab issue
                        dataMapping = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.ResolvedReleaseId.Value, releaseMappings);
                        if (dataMapping == null)
                        {
                            //Add a new mapping entry
                            dataMapping = new RemoteDataMapping();
                            dataMapping.ProjectId = projectId;
                            dataMapping.InternalId = remoteIncident.ResolvedReleaseId.Value;
                            dataMapping.ExternalKey = milestone.milestoneId + "";
                            newReleaseMappings.Add(dataMapping);
                        }

                        //only change release if we have to
                        int externalReleaseId = Int32.Parse(dataMapping.ExternalKey);
                        if (milestone != null && externalReleaseId == milestone.milestoneId)
                        {
                            InternalFunctions.setGitLabIssueMilestone(externalProjectId, this, externalIssue, milestone);
                        }
                    }

                }
            }
            #endregion
        }

        /// <summary>
        /// Processes a single external bug record and either adds or updates it in SpiraTest
        /// </summary>
        /// <param name="projectId">The id of the current project</param>
        /// <param name="spiraSoapService">The Spira API proxy class</param>
        /// <param name="externalSystemBug">The external bug object</param>
        /// <param name="newIncidentMappings">The list of any new incidents to be mapped</param>
        /// <param name="newReleaseMappings">The list of any new releases to be mapped</param>
        /// <param name="oldReleaseMappings">The list list of old releases to be un-mapped</param>
        /// <param name="customPropertyMappingList">The mapping of custom properties</param>
        /// <param name="customPropertyValueMappingList">The mapping of custom property list values</param>
        /// <param name="incidentCustomProperties">The list of incident custom properties defined for this project</param>
        /// <param name="incidentMappings">The list of existing mapped incidents</param>
        /// <param name="externalProjectId">The id of the project in the external system</param>
        /// <param name="productName">The name of the product being connected to (SpiraTest, SpiraPlan, etc.)</param>
        /// <param name="severityMappings">The incident severity mappings</param>
        /// <param name="priorityMappings">The incident priority mappings</param>
        /// <param name="statusMappings">The incident status mappings</param>
        /// <param name="typeMappings">The incident type mappings</param>
        /// <param name="userMappings">The incident user mappings</param>
        /// <param name="releaseMappings">The release mappings</param>
        private void ProcessExternalBug(int projectId, SoapServiceClient spiraSoapService, GitLabIssue externalSystemBug, List<RemoteDataMapping> newIncidentMappings, List<RemoteDataMapping> newReleaseMappings, List<RemoteDataMapping> oldReleaseMappings, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteCustomProperty[] incidentCustomProperties, RemoteDataMapping[] incidentMappings, string externalProjectId, string productName, RemoteDataMapping[] severityMappings, RemoteDataMapping[] priorityMappings, RemoteDataMapping[] statusMappings, RemoteDataMapping[] typeMappings, RemoteDataMapping[] userMappings, RemoteDataMapping[] releaseMappings)
        {
            string externalBugId = externalSystemBug.IId + "";
            string externalBugName = externalSystemBug.Title;
            //convert MD into HTML
            string externalBugDescription = CommonMark.CommonMarkConverter.Convert(externalSystemBug.Description);
            string externalBugProjectId = externalProjectId;
            string externalBugCreator = externalSystemBug.creator.username;
            string externalBugStatus = externalSystemBug.State;
            //leave empty for default type in Spira
            //string externalBugType = "";
            //TODO: Multiple authors
            string externalBugAssignee = "";
            if (externalSystemBug.Assignees.Count > 0)
            {
                externalBugAssignee = externalSystemBug.Assignees[0].username;
            }

            string externalBugResolvedRelease = null;
            if (externalSystemBug.Milestone != null)
            {
                externalBugResolvedRelease = externalSystemBug.Milestone.milestoneId + "";
            }

            DateTime? externalBugStartDate = null;  //Not supported
            DateTime? externalBugClosedDate = externalSystemBug.ClosedAt;

            ////GitLab has nothing about these fields
            //string externalBugDetectedRelease = "";
            //string externalBugPriority = "";
            //string externalBugSeverity = "";
            //int? externalEstimatedEffortInMinutes = null;
            //int? externalActualEffortInMinutes = null;
            //int? externalRemainingEffortInMinutes = null;

            //Make sure the projects match (i.e. the external bug is in the project being synced)
            //It should be handled previously in the filter sent to external system, but use this as an extra check
            if (externalBugProjectId == externalProjectId)
            {
                //See if we have an existing mapping or not
                RemoteDataMapping incidentMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalBugId, incidentMappings, false);

                int incidentId = -1;
                RemoteIncident remoteIncident = null;
                if (incidentMapping == null)
                {
                    //This bug needs to be inserted into SpiraTest
                    remoteIncident = new RemoteIncident();
                    remoteIncident.ProjectId = projectId;

                    //Set the name for new incidents
                    if (String.IsNullOrEmpty(externalBugName))
                    {
                        remoteIncident.Name = "Name Not Specified";
                    }
                    else
                    {
                        remoteIncident.Name = externalBugName;
                    }

                    //Set the description for new incidents
                    if (String.IsNullOrEmpty(externalBugDescription))
                    {
                        remoteIncident.Description = "Description Not Specified";
                    }
                    else
                    {
                        remoteIncident.Description = externalBugDescription;
                    }

                    //Set the dectector for new incidents
                    if (!String.IsNullOrEmpty(externalBugCreator))
                    {
                        RemoteDataMapping dataMapping = FindUserMappingByExternalKey(externalBugCreator, userMappings, spiraSoapService);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for " + EXTERNAL_SYSTEM_NAME + " user " + externalBugCreator + " so using synchronization user as detector.", EventLogEntryType.Warning);
                        }
                        else
                        {
                            remoteIncident.OpenerId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the detector " + remoteIncident.OpenerId.ToString() + "\n", EventLogEntryType.Information);
                        }
                    }

                    //actually create the new incident
                    RemoteIncident newIncident = spiraSoapService.Incident_Create(remoteIncident);
                    incidentId = newIncident.IncidentId.Value;
                    RemoteDataMapping mapping = new RemoteDataMapping();
                    mapping.ExternalKey = externalBugId;
                    mapping.InternalId = incidentId;
                    newIncidentMappings.Add(mapping);

                    //Try adding a link to the issue in GitLab
                    try
                    {
                        string externalUrl = externalSystemBug.WebUrl;
                        if (!String.IsNullOrEmpty(externalUrl))
                        {
                            List<RemoteLinkedArtifact> linkedArtifacts = new List<RemoteLinkedArtifact>();
                            linkedArtifacts.Add(new RemoteLinkedArtifact() { ArtifactId = incidentId, ArtifactTypeId = (int)Constants.ArtifactType.Incident });
                            RemoteDocument remoteUrl = new RemoteDocument();
                            remoteUrl.AttachedArtifacts = linkedArtifacts.ToArray();
                            remoteUrl.Description = "Link to issue in " + EXTERNAL_SYSTEM_NAME;
                            remoteUrl.FilenameOrUrl = externalUrl;
                            spiraSoapService.Document_AddUrl(remoteUrl);
                        }
                    }
                    catch (Exception exception)
                    {
                        //Log a message that describes why it's not working
                        LogErrorEvent("Unable to add " + EXTERNAL_SYSTEM_NAME + " hyperlink to the " + productName + " incident, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                        //Just continue with the rest since it's optional.
                    }

                    //Re-retrieve the incident to make sure we have everything the server did
                    remoteIncident = spiraSoapService.Incident_RetrieveById(incidentId);
                }
                else
                {
                    //We need to load the matching SpiraTest incident and update
                    incidentId = incidentMapping.InternalId;

                    //Now retrieve the SpiraTest incident using the Import APIs
                    try
                    {
                        remoteIncident = spiraSoapService.Incident_RetrieveById(incidentId);


                        //Update the name for existing incidents
                        if (!String.IsNullOrEmpty(externalBugName))
                        {
                            remoteIncident.Name = externalBugName;
                        }

                        //Update the description for existing incidents
                        if (!String.IsNullOrEmpty(externalBugDescription))
                        {
                            remoteIncident.Description = externalBugDescription;
                        }
                    }
                    catch (Exception)
                    {
                        //Ignore as it will leave the remoteIncident as null
                    }
                }

                try
                {
                    //Make sure we have retrieved or created the incident
                    if (remoteIncident != null)
                    {
                        RemoteDataMapping dataMapping;
                        LogTraceEvent(eventLog, "Retrieved incident in " + productName + "\n", EventLogEntryType.Information);

                        //Now get the bug status from the mapping
                        dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalBugStatus, statusMappings, true);
                        RemoteDataMapping mappedStatus = InternalFunctions.FindMappingByInternalId(projectId, remoteIncident.IncidentStatusId.Value, statusMappings);
                        if (dataMapping == null)
                        {
                            //We can't find the matching item so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for " + EXTERNAL_SYSTEM_NAME + " bug status '" + externalBugStatus + "' in project PR, so not changing status." + projectId, EventLogEntryType.Warning);
                        }

                        //only change incident status if we need to
                        else if (mappedStatus.ExternalKey != externalBugStatus)
                        {
                            remoteIncident.IncidentStatusId = dataMapping.InternalId;
                        }

                        LogTraceEvent(eventLog, "Got the status\n", EventLogEntryType.Information);

                        ////Now get the bug type from the mapping
                        //if (!String.IsNullOrEmpty(externalBugType))
                        //{
                        //    dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalBugType, typeMappings, true);
                        //    if (dataMapping == null)
                        //    {
                        //        //If this is a new issue and we don't have the type mapped
                        //        //it means that they don't want them getting added to SpiraTest
                        //        if (incidentId == -1)
                        //        {
                        //            return;
                        //        }
                        //        //We can't find the matching item so log and ignore
                        //        eventLog.WriteEntry("Unable to locate mapping entry for " + EXTERNAL_SYSTEM_NAME + " incident type " + externalBugType + " in project PR" + projectId, EventLogEntryType.Error);
                        //    }
                        //    else
                        //    {
                        //        remoteIncident.IncidentTypeId = dataMapping.InternalId;
                        //    }
                        //}
                        //LogTraceEvent(eventLog, "Got the type\n", EventLogEntryType.Information);

                        //Now update the bug's owner/assignee in SpiraTest
                        /* dataMapping = FindUserMappingByExternalKey(externalBugAssignee, userMappings, spiraSoapService);
                        if (dataMapping == null)
                        {
                            //We can't find the matching user so log and ignore
                            LogErrorEvent("Unable to locate mapping entry for " + EXTERNAL_SYSTEM_NAME + " user " + externalBugAssignee + " so ignoring the assignee change", EventLogEntryType.Error);
                        }
                        else
                        {
                            remoteIncident.OwnerId = dataMapping.InternalId;
                            LogTraceEvent(eventLog, "Got the assignee " + remoteIncident.OwnerId.ToString() + "\n", EventLogEntryType.Information);
                        } */

                        //Update the start-date if necessary
                        if (externalBugStartDate.HasValue)
                        {
                            remoteIncident.StartDate = externalBugStartDate.Value;
                        }

                        //Update the closed-date if necessary
                        if (externalBugClosedDate.HasValue)
                        {
                            remoteIncident.ClosedDate = externalBugClosedDate.Value;
                        }

                        //Now we need to get all the comments attached to the bug in the external system
                        List<GitLabComment> externalBugComments = externalSystemBug.CommentsList;

                        //Now get the list of comments attached to the SpiraTest incident
                        //If this is the new incident case, just leave as null
                        RemoteComment[] incidentComments = null;
                        if (incidentId != -1)
                        {
                            incidentComments = spiraSoapService.Incident_RetrieveComments(incidentId);
                        }

                        //Iterate through all the comments and see if we need to add any to SpiraTest
                        List<RemoteComment> newIncidentComments = new List<RemoteComment>();
                        if (externalBugComments != null)
                        {
                            foreach (GitLabComment externalBugComment in externalBugComments)
                            {
                                //Extract the resolution values from the external system

                                //convert MD to HTML
                                string externalCommentText = CommonMark.CommonMarkConverter.Convert(externalBugComment.body);
                                string externalCommentCreator = externalBugComment.commenter.userId + "";

                                //See if we already have this resolution inside SpiraTest
                                bool alreadyAdded = false;
                                if (incidentComments != null)
                                {
                                    if (!InternalFunctions.canPostComment(externalCommentText) || !InternalFunctions.noCommentExists(externalCommentText, incidentComments, this))
                                    {
                                        alreadyAdded = true;
                                    }
                                }
                                if (!alreadyAdded && !externalBugComment.system)
                                {
                                    //Get the resolution author mapping
                                    LogTraceEvent(eventLog, "Looking for " + EXTERNAL_SYSTEM_NAME + " comments creator: '" + externalCommentCreator + "'\n", EventLogEntryType.Information);
                                    dataMapping = InternalFunctions.FindMappingByExternalKey(externalCommentCreator, userMappings);
                                    int? creatorId = null;
                                    if (dataMapping != null)
                                    {
                                        //Set the creator of the comment, otherwise leave null and SpiraTest will
                                        //simply use the synchronization user
                                        creatorId = dataMapping.InternalId;
                                    }

                                    //Add the comment to SpiraTest
                                    RemoteComment newIncidentComment = new RemoteComment();
                                    newIncidentComment.ArtifactId = incidentId;
                                    newIncidentComment.UserId = creatorId;
                                    newIncidentComment.CreationDate = DateTime.UtcNow;
                                    newIncidentComment.Text = InternalFunctions.addUsername(externalCommentText, externalBugComment.commenter.username);
                                    newIncidentComments.Add(newIncidentComment);
                                }
                            }
                        }
                        //The resolutions will actually get added later when we insert/update the incident record itself

                        //Debug logging - comment out for production code
                        LogTraceEvent(eventLog, "Got the comments/resolution\n", EventLogEntryType.Information);

                        //Specify the resolved-in release if applicable
                        if (!String.IsNullOrEmpty(externalBugResolvedRelease))
                        {
                            //See if we have a mapped SpiraTest release in either the existing list of
                            //mapped releases or the list of newly added ones
                            dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalBugResolvedRelease, releaseMappings, false);
                            if (dataMapping == null)
                            {
                                dataMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalBugResolvedRelease, newReleaseMappings.ToArray(), false);
                            }
                            if (dataMapping == null)
                            {
                                //We can't find the matching item so need to create a new release in SpiraTest and add to mappings
                                GitLabMilestone milestone = null;

                                //get the milestone from the list of milestones
                                foreach (GitLabMilestone m in milestones)
                                {
                                    if (m.milestoneId + "" == externalBugResolvedRelease)
                                    {
                                        milestone = m;
                                    }
                                }

                                string externalReleaseName = milestone.name;
                                DateTime externalReleaseStartDate = milestone.creationDate;
                                DateTime externalReleaseEndDate = milestone.dueDate;
                                // N/A
                                string externalReleaseVersionNumber = "";

                                LogTraceEvent(eventLog, "Adding new release in " + productName + " for version " + externalBugResolvedRelease + "\n", EventLogEntryType.Information);
                                RemoteRelease remoteRelease = new RemoteRelease();
                                remoteRelease.Name = externalReleaseName;
                                if (externalReleaseVersionNumber.Length > 10)
                                {
                                    remoteRelease.VersionNumber = externalReleaseVersionNumber.Substring(0, 10);
                                }
                                else
                                {
                                    remoteRelease.VersionNumber = externalReleaseVersionNumber;
                                }
                                remoteRelease.Active = true;

                                //no date can be before here
                                DateTime tooEarly = new DateTime(1776, 7, 4);

                                //If no start-date specified, simply use now
                                remoteRelease.StartDate = (externalReleaseStartDate > tooEarly) ? externalReleaseStartDate : DateTime.UtcNow;
                                //If no end-date specified, simply use 1-month from now
                                remoteRelease.EndDate = (externalReleaseEndDate > tooEarly) ? externalReleaseEndDate : DateTime.UtcNow.AddMonths(1);
                                remoteRelease.CreatorId = remoteIncident.OpenerId;
                                remoteRelease.CreationDate = DateTime.UtcNow;
                                remoteRelease.ResourceCount = 1;
                                remoteRelease.LastUpdateDate = DateTime.UtcNow;

                                //2 is in progress, 3 is completed
                                int releaseStatus = (milestone.status == "active") ? 2 : 3;
                                remoteRelease.ReleaseStatusId = releaseStatus;
                                //3 is an iteration
                                remoteRelease.ReleaseTypeId = 3;
                                remoteRelease = spiraSoapService.Release_Create(remoteRelease, null);

                                //Add a new mapping entry
                                RemoteDataMapping newReleaseMapping = new RemoteDataMapping();
                                newReleaseMapping.ProjectId = projectId;
                                newReleaseMapping.InternalId = remoteRelease.ReleaseId.Value;
                                newReleaseMapping.ExternalKey = externalBugResolvedRelease;
                                newReleaseMappings.Add(newReleaseMapping);
                                remoteIncident.ResolvedReleaseId = newReleaseMapping.InternalId;
                                LogTraceEvent(eventLog, "Setting resolved release id to  " + newReleaseMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                            }
                            else
                            {
                                remoteIncident.ResolvedReleaseId = dataMapping.InternalId;
                                LogTraceEvent(eventLog, "Setting resolved release id to  " + dataMapping.InternalId + "\n", EventLogEntryType.SuccessAudit);
                            }
                        }

                        /*
                         * TODO: Need to get the list of custom property values for the bug from the external system.
                         * The following sample code just stores them in a simple text dictionary
                         * where the Key=Field Name, Value=Field Value
                         */
                        /* Dictionary<string, object> externalSystemCustomFieldValues = null;  //TODO: Replace with real code

                        //Now we need to see if any of the custom fields have changed in the external system bug
                        if (remoteIncident.CustomProperties != null && remoteIncident.CustomProperties.Length > 0)
                        {
                            ProcessExternalSystemCustomFields(productName, projectId, remoteIncident, externalSystemCustomFieldValues, incidentCustomProperties, customPropertyMappingList, customPropertyValueMappingList, userMappings, spiraSoapService);
                        } */

                        //Finally add or update the incident in SpiraTest
                        if (incidentId == -1)
                        {
                            //Debug logging - comment out for production code
                            try
                            {
                                remoteIncident = spiraSoapService.Incident_Create(remoteIncident);

                                //Extract the SpiraTest incident and add to mappings table
                                RemoteDataMapping newIncidentMapping = new RemoteDataMapping();
                                newIncidentMapping.ProjectId = projectId;
                                newIncidentMapping.InternalId = remoteIncident.IncidentId.Value;
                                newIncidentMapping.ExternalKey = externalBugId;
                                newIncidentMappings.Add(newIncidentMapping);

                                //Now add any comments (need to set the ID)
                                foreach (RemoteComment newIncidentComment in newIncidentComments)
                                {
                                    newIncidentComment.ArtifactId = remoteIncident.IncidentId.Value;
                                }
                                spiraSoapService.Incident_AddComments(newIncidentComments.ToArray());

                                //Try adding a link to the issue in GitLab
                                try
                                {
                                    string externalUrl = externalSystemBug.WebUrl;
                                    if (!String.IsNullOrEmpty(externalUrl))
                                    {
                                        List<RemoteLinkedArtifact> linkedArtifacts = new List<RemoteLinkedArtifact>();
                                        linkedArtifacts.Add(new RemoteLinkedArtifact() { ArtifactId = remoteIncident.IncidentId.Value, ArtifactTypeId = (int)Constants.ArtifactType.Incident });
                                        RemoteDocument remoteUrl = new RemoteDocument();
                                        remoteUrl.AttachedArtifacts = linkedArtifacts.ToArray();
                                        remoteUrl.Description = "Link to issue in " + EXTERNAL_SYSTEM_NAME;
                                        remoteUrl.FilenameOrUrl = externalUrl;
                                        spiraSoapService.Document_AddUrl(remoteUrl);
                                    }
                                }
                                catch (Exception exception)
                                {
                                    //Log a message that describes why it's not working
                                    LogErrorEvent("Unable to add " + EXTERNAL_SYSTEM_NAME + " hyperlink to the " + productName + " incident, error was: " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Warning);
                                    //Just continue with the rest since it's optional.
                                }
                            }
                            catch (Exception exception)
                            {
                                LogErrorEvent("Error Adding " + EXTERNAL_SYSTEM_NAME + " bug " + externalBugId + " to " + productName + " (" + exception.Message + ")\n" + exception.StackTrace, EventLogEntryType.Error);
                                return;
                            }
                            LogTraceEvent(eventLog, "Successfully added " + EXTERNAL_SYSTEM_NAME + " bug " + externalBugId + " to " + productName + "\n", EventLogEntryType.Information);

                        }
                        else
                        {
                            spiraSoapService.Incident_Update(remoteIncident);

                            //Now add any resolutions
                            spiraSoapService.Incident_AddComments(newIncidentComments.ToArray());

                            //Debug logging - comment out for production code
                            LogTraceEvent(eventLog, "Successfully updated\n", EventLogEntryType.Information);
                        }
                    }
                }
                catch (FaultException<ValidationFaultMessage> validationException)
                {
                    string message = "";
                    ValidationFaultMessage validationFaultMessage = validationException.Detail;
                    message = validationFaultMessage.Summary + ": \n";
                    {
                        foreach (ValidationFaultMessageItem messageItem in validationFaultMessage.Messages)
                        {
                            message += messageItem.FieldName + "=" + messageItem.Message + " \n";
                        }
                    }
                    LogErrorEvent("Validation Error Inserting/Updating " + EXTERNAL_SYSTEM_NAME + " Bug " + externalBugId + " in " + productName + " (" + message + ")\n" + validationException.StackTrace, EventLogEntryType.Error);
                }
                catch (Exception exception)
                {
                    //Log and continue execution
                    LogErrorEvent("Error Inserting/Updating " + EXTERNAL_SYSTEM_NAME + " Bug " + externalBugId + " in " + productName + ": " + exception.Message + "\n" + exception.StackTrace, EventLogEntryType.Error);
                }
            }
        }

        /// <summary>
        /// Updates the Spira incident object's custom properties with any new/changed custom fields from the external bug object
        /// </summary>
        /// <param name="projectId">The id of the current project</param>
        /// <param name="spiraSoapService">The Spira API proxy class</param>
        /// <param name="remoteArtifact">The Spira artifact</param>
        /// <param name="customPropertyMappingList">The mapping of custom properties</param>
        /// <param name="customPropertyValueMappingList">The mapping of custom property list values</param>
        /// <param name="externalSystemCustomFieldValues">The list of custom fields in the external system</param>
        /// <param name="productName">The name of the product being connected to (SpiraTest, SpiraPlan, etc.)</param>
        /// <param name="userMappings">The user mappings</param>
        private void ProcessExternalSystemCustomFields(string productName, int projectId, RemoteArtifact remoteArtifact, Dictionary<string, object> externalSystemCustomFieldValues, RemoteCustomProperty[] customProperties, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteDataMapping[] userMappings, SoapServiceClient spiraSoapService)
        {
            //Loop through all the defined Spira custom properties
            foreach (RemoteCustomProperty customProperty in customProperties)
            {
                //Get the external key of this custom property (if it has one)
                if (customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                {
                    RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                    if (customPropertyDataMapping != null)
                    {
                        LogTraceEvent(eventLog, "Found custom property mapping for " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + "\n", EventLogEntryType.Information);
                        string externalKey = customPropertyDataMapping.ExternalKey;
                        //See if we have a list, multi-list or user custom field as they need to be handled differently
                        if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List)
                        {
                            LogTraceEvent(eventLog, EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " is mapped to a LIST property\n", EventLogEntryType.Information);

                            //Now we need to set the value on the SpiraTest incident
                            if (externalSystemCustomFieldValues.ContainsKey(externalKey))
                            {
                                if (externalSystemCustomFieldValues[externalKey] == null)
                                {
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (int?)null);
                                }
                                else
                                {
                                    //Need to get the Spira custom property value
                                    string fieldValue = externalSystemCustomFieldValues[externalKey].ToString();
                                    RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                    RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, fieldValue, customPropertyValueMappings, false);
                                    if (customPropertyValueMapping != null)
                                    {
                                        InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueMapping.InternalId);
                                    }
                                }
                            }
                            else
                            {
                                LogErrorEvent(String.Format("" + EXTERNAL_SYSTEM_NAME + " bug doesn't have a field definition for '{0}'\n", externalKey), EventLogEntryType.Warning);
                            }
                        }
                        else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.User)
                        {
                            LogTraceEvent(eventLog, EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " is mapped to a USER property\n", EventLogEntryType.Information);

                            //Now we need to set the value on the SpiraTest incident
                            if (externalSystemCustomFieldValues.ContainsKey(externalKey))
                            {
                                if (externalSystemCustomFieldValues[externalKey] == null)
                                {
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (int?)null);
                                }
                                else
                                {
                                    //Need to get the Spira custom property value
                                    string fieldValue = externalSystemCustomFieldValues[externalKey].ToString();
                                    RemoteDataMapping customPropertyValueMapping = FindUserMappingByExternalKey(fieldValue, userMappings, spiraSoapService);
                                    if (customPropertyValueMapping != null)
                                    {
                                        InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, customPropertyValueMapping.InternalId);
                                    }
                                }
                            }
                            else
                            {
                                LogErrorEvent(String.Format("" + EXTERNAL_SYSTEM_NAME + " bug doesn't have a field definition for '{0}'\n", externalKey), EventLogEntryType.Warning);
                            }
                        }
                        else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                        {
                            LogTraceEvent(eventLog, EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " is mapped to a MULTILIST property\n", EventLogEntryType.Information);

                            //Next the multi-list fields
                            //Now we need to set the value on the SpiraTest incident
                            if (externalSystemCustomFieldValues.ContainsKey(externalKey))
                            {
                                if (externalSystemCustomFieldValues[externalKey] == null)
                                {
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (List<int>)null);
                                }
                                else
                                {
                                    //Need to get the Spira custom property value
                                    List<string> externalCustomFieldValues = (List<string>)externalSystemCustomFieldValues[externalKey];
                                    RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];

                                    //Data-map each of the custom property values
                                    //We assume that the external system has a multiselect stored list of string values (List<string>)
                                    List<int> spiraCustomValueIds = new List<int>();
                                    foreach (string externalCustomFieldValue in externalCustomFieldValues)
                                    {
                                        RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByExternalKey(projectId, externalCustomFieldValue, customPropertyValueMappings, false);
                                        if (customPropertyValueMapping != null)
                                        {
                                            spiraCustomValueIds.Add(customPropertyValueMapping.InternalId);
                                        }
                                    }
                                    InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, spiraCustomValueIds);
                                }
                            }
                            else
                            {
                                LogErrorEvent(String.Format("" + EXTERNAL_SYSTEM_NAME + " bug doesn't have a field definition for '{0}'\n", externalKey), EventLogEntryType.Warning);
                            }
                        }
                        else
                        {
                            LogTraceEvent(eventLog, EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " is mapped to a VALUE property\n", EventLogEntryType.Information);

                            //Now we need to set the value on the SpiraTest artifact
                            if (externalSystemCustomFieldValues.ContainsKey(externalKey))
                            {
                                switch ((Constants.CustomPropertyType)customProperty.CustomPropertyTypeId)
                                {
                                    case Constants.CustomPropertyType.Boolean:
                                        {
                                            if (externalSystemCustomFieldValues[externalKey] == null || !(externalSystemCustomFieldValues[externalKey] is Boolean))
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (bool?)null);
                                            }
                                            else
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (bool)externalSystemCustomFieldValues[externalKey]);
                                                LogTraceEvent(eventLog, "Setting " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " value '" + externalSystemCustomFieldValues[externalKey] + "' on artifact\n", EventLogEntryType.Information);
                                            }
                                        }
                                        break;

                                    case Constants.CustomPropertyType.Date:
                                        {
                                            if (externalSystemCustomFieldValues[externalKey] == null || !(externalSystemCustomFieldValues[externalKey] is DateTime))
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (DateTime?)null);
                                            }
                                            else
                                            {
                                                //Need to convert to UTC for Spira
                                                DateTime localTime = (DateTime)externalSystemCustomFieldValues[externalKey];
                                                DateTime utcTime = localTime.ToUniversalTime();

                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, utcTime);
                                                LogTraceEvent(eventLog, "Setting " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " value '" + utcTime + "' on artifact\n", EventLogEntryType.Information);
                                            }
                                        }
                                        break;


                                    case Constants.CustomPropertyType.Decimal:
                                        {
                                            if (externalSystemCustomFieldValues[externalKey] == null || !(externalSystemCustomFieldValues[externalKey] is Decimal))
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (decimal?)null);
                                            }
                                            else
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (decimal)externalSystemCustomFieldValues[externalKey]);
                                                LogTraceEvent(eventLog, "Setting " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " value '" + externalSystemCustomFieldValues[externalKey] + "' on artifact\n", EventLogEntryType.Information);
                                            }
                                        }
                                        break;

                                    case Constants.CustomPropertyType.Integer:
                                        {
                                            if (externalSystemCustomFieldValues[externalKey] == null || !(externalSystemCustomFieldValues[externalKey] is Int32))
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (int?)null);
                                            }
                                            else
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (int)externalSystemCustomFieldValues[externalKey]);
                                                LogTraceEvent(eventLog, "Setting " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " value '" + externalSystemCustomFieldValues[externalKey] + "' on artifact\n", EventLogEntryType.Information);
                                            }
                                        }
                                        break;

                                    case Constants.CustomPropertyType.Text:
                                    default:
                                        {
                                            if (externalSystemCustomFieldValues[externalKey] == null)
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, (string)null);
                                            }
                                            else
                                            {
                                                InternalFunctions.SetCustomPropertyValue(remoteArtifact, customProperty.PropertyNumber, externalSystemCustomFieldValues[externalKey].ToString());
                                                LogTraceEvent(eventLog, "Setting " + EXTERNAL_SYSTEM_NAME + " field " + customPropertyDataMapping.ExternalKey + " value '" + externalSystemCustomFieldValues[externalKey].ToString() + "' on artifact\n", EventLogEntryType.Information);
                                            }
                                        }
                                        break;
                                }
                            }
                            else
                            {
                                LogErrorEvent(String.Format("" + EXTERNAL_SYSTEM_NAME + " bug doesn't have a field definition for '{0}'\n", externalKey), EventLogEntryType.Warning);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates the external bug object with any incident custom property values
        /// </summary>
        /// <param name="projectId">The id of the current project</param>
        /// <param name="spiraSoapService">The Spira API proxy class</param>
        /// <param name="remoteArtifact">The Spira artifact</param>
        /// <param name="customPropertyMappingList">The mapping of custom properties</param>
        /// <param name="customPropertyValueMappingList">The mapping of custom property list values</param>
        /// <param name="externalSystemCustomFieldValues">The list of custom fields in the external system</param>
        /// <param name="productName">The name of the product being connected to (SpiraTest, SpiraPlan, etc.)</param>
        /// <param name="userMappings">The user mappings</param>
        private void ProcessCustomProperties(string productName, int projectId, RemoteArtifact remoteArtifact, Dictionary<string, object> externalSystemCustomFieldValues, Dictionary<int, RemoteDataMapping> customPropertyMappingList, Dictionary<int, RemoteDataMapping[]> customPropertyValueMappingList, RemoteDataMapping[] userMappings, SoapServiceClient spiraSoapService)
        {
            foreach (RemoteArtifactCustomProperty artifactCustomProperty in remoteArtifact.CustomProperties)
            {
                //Handle user, list and non-list separately since only the list types need to have value mappings
                RemoteCustomProperty customProperty = artifactCustomProperty.Definition;
                if (customProperty != null && customProperty.CustomPropertyId.HasValue)
                {
                    if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.List)
                    {
                        //Single-Select List
                        LogTraceEvent(eventLog, "Checking list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                        //See if we have a custom property value set
                        //Get the corresponding external custom field (if there is one)
                        if (artifactCustomProperty.IntegerValue.HasValue && customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                        {
                            LogTraceEvent(eventLog, "Got value for list custom property: " + customProperty.Name + " (" + artifactCustomProperty.IntegerValue.Value + ")\n", EventLogEntryType.Information);
                            RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                            if (customPropertyDataMapping != null)
                            {
                                string externalCustomField = customPropertyDataMapping.ExternalKey;

                                //Get the corresponding external custom field value (if there is one)
                                if (!String.IsNullOrEmpty(externalCustomField) && customPropertyValueMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                {
                                    RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                    if (customPropertyValueMappings != null)
                                    {
                                        RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByInternalId(projectId, artifactCustomProperty.IntegerValue.Value, customPropertyValueMappings);
                                        if (customPropertyValueMapping != null)
                                        {
                                            string externalCustomFieldValue = customPropertyValueMapping.ExternalKey;

                                            //Make sure we have a mapped custom field in the external system
                                            if (!String.IsNullOrEmpty(externalCustomFieldValue))
                                            {
                                                LogTraceEvent(eventLog, "The custom property corresponds to the " + EXTERNAL_SYSTEM_NAME + " '" + externalCustomField + "' field", EventLogEntryType.Information);
                                                externalSystemCustomFieldValues[externalCustomField] = externalCustomFieldValue;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.MultiList)
                    {
                        //Multi-Select List
                        LogTraceEvent(eventLog, "Checking multi-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                        //See if we have a custom property value set
                        //Get the corresponding external custom field (if there is one)
                        if (artifactCustomProperty.IntegerListValue != null && artifactCustomProperty.IntegerListValue.Length > 0 && customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                        {
                            LogTraceEvent(eventLog, "Got values for multi-list custom property: " + customProperty.Name + " (Count=" + artifactCustomProperty.IntegerListValue.Length + ")\n", EventLogEntryType.Information);
                            RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                            if (customPropertyDataMapping != null && !String.IsNullOrEmpty(customPropertyDataMapping.ExternalKey))
                            {
                                string externalCustomField = customPropertyDataMapping.ExternalKey;
                                LogTraceEvent(eventLog, "Got external key for multi-list custom property: " + customProperty.Name + " = " + externalCustomField + "\n", EventLogEntryType.Information);

                                //Loop through each value in the list
                                List<string> externalCustomFieldValues = new List<string>();
                                foreach (int customPropertyListValue in artifactCustomProperty.IntegerListValue)
                                {
                                    //Get the corresponding external custom field value (if there is one)
                                    if (customPropertyValueMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                                    {
                                        RemoteDataMapping[] customPropertyValueMappings = customPropertyValueMappingList[customProperty.CustomPropertyId.Value];
                                        if (customPropertyValueMappings != null)
                                        {
                                            RemoteDataMapping customPropertyValueMapping = InternalFunctions.FindMappingByInternalId(projectId, customPropertyListValue, customPropertyValueMappings);
                                            if (customPropertyValueMapping != null)
                                            {
                                                LogTraceEvent(eventLog, "Added multi-list custom property field value: " + customProperty.Name + " (Value=" + customPropertyValueMapping.ExternalKey + ")\n", EventLogEntryType.Information);
                                                externalCustomFieldValues.Add(customPropertyValueMapping.ExternalKey);
                                            }
                                        }
                                    }
                                }

                                //Make sure that we have some values to set
                                LogTraceEvent(eventLog, "Got mapped values for multi-list custom property: " + customProperty.Name + " (Count=" + externalCustomFieldValues.Count + ")\n", EventLogEntryType.Information);
                                if (externalCustomFieldValues.Count > 0)
                                {
                                    externalSystemCustomFieldValues[externalCustomField] = externalCustomFieldValues;
                                }
                                else
                                {
                                    externalSystemCustomFieldValues[externalCustomField] = null;
                                }
                            }
                        }
                    }
                    else if (customProperty.CustomPropertyTypeId == (int)Constants.CustomPropertyType.User)
                    {
                        //User
                        LogTraceEvent(eventLog, "Checking user custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                        //See if we have a custom property value set
                        if (artifactCustomProperty.IntegerValue.HasValue)
                        {
                            RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                            if (customPropertyDataMapping != null && !String.IsNullOrEmpty(customPropertyDataMapping.ExternalKey))
                            {
                                string externalCustomField = customPropertyDataMapping.ExternalKey;
                                LogTraceEvent(eventLog, "Got external key for user custom property: " + customProperty.Name + " = " + externalCustomField + "\n", EventLogEntryType.Information);

                                LogTraceEvent(eventLog, "Got value for user custom property: " + customProperty.Name + " (" + artifactCustomProperty.IntegerValue.Value + ")\n", EventLogEntryType.Information);
                                //Get the corresponding external system user (if there is one)
                                RemoteDataMapping dataMapping = FindUserMappingByInternalId(artifactCustomProperty.IntegerValue.Value, userMappings, spiraSoapService);
                                if (dataMapping != null)
                                {
                                    string externalUserName = dataMapping.ExternalKey;
                                    LogTraceEvent(eventLog, "Adding user custom property field value: " + customProperty.Name + " (Value=" + externalUserName + ")\n", EventLogEntryType.Information);
                                    LogTraceEvent(eventLog, "The custom property corresponds to the " + EXTERNAL_SYSTEM_NAME + " '" + externalCustomField + "' field", EventLogEntryType.Information);
                                    externalSystemCustomFieldValues[externalCustomField] = externalUserName;
                                }
                                else
                                {
                                    LogErrorEvent("Unable to find a matching " + EXTERNAL_SYSTEM_NAME + " user for " + productName + " user with ID=" + artifactCustomProperty.IntegerValue.Value + " so leaving property null.", EventLogEntryType.Warning);
                                }
                            }
                        }
                    }
                    else
                    {
                        //Other
                        LogTraceEvent(eventLog, "Checking non-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);

                        //See if we have a custom property value set
                        if (!String.IsNullOrEmpty(artifactCustomProperty.StringValue) || artifactCustomProperty.BooleanValue.HasValue
                            || artifactCustomProperty.DateTimeValue.HasValue || artifactCustomProperty.DecimalValue.HasValue
                            || artifactCustomProperty.IntegerValue.HasValue)
                        {
                            LogTraceEvent(eventLog, "Got value for non-list custom property: " + customProperty.Name + "\n", EventLogEntryType.Information);
                            //Get the corresponding external custom field (if there is one)
                            if (customPropertyMappingList != null && customPropertyMappingList.ContainsKey(customProperty.CustomPropertyId.Value))
                            {
                                RemoteDataMapping customPropertyDataMapping = customPropertyMappingList[customProperty.CustomPropertyId.Value];
                                if (customPropertyDataMapping != null)
                                {
                                    string externalCustomField = customPropertyDataMapping.ExternalKey;

                                    //Make sure we have a mapped custom field in the external system mapped
                                    if (!String.IsNullOrEmpty(externalCustomField))
                                    {
                                        LogTraceEvent(eventLog, "The custom property corresponds to the " + EXTERNAL_SYSTEM_NAME + " '" + externalCustomField + "' field", EventLogEntryType.Information);
                                        object customFieldValue = InternalFunctions.GetCustomPropertyValue(artifactCustomProperty);
                                        externalSystemCustomFieldValues[externalCustomField] = customFieldValue;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds a user mapping entry from the internal id
        /// </summary>
        /// <param name="internalId">The internal id</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>If we are auto-mapping users, it will lookup the user-id instead</remarks>
        protected RemoteDataMapping FindUserMappingByInternalId(int internalId, RemoteDataMapping[] dataMappings, SoapServiceClient client)
        {
            if (this.autoMapUsers)
            {
                RemoteUser remoteUser = client.User_RetrieveById(internalId);
                if (remoteUser == null)
                {
                    return null;
                }
                RemoteDataMapping userMapping = new RemoteDataMapping();
                userMapping.InternalId = remoteUser.UserId.Value;
                userMapping.ExternalKey = remoteUser.UserName;
                return userMapping;
            }
            else
            {
                return InternalFunctions.FindMappingByInternalId(internalId, dataMappings);
            }
        }

        /// <summary>
        /// Finds a user mapping entry from the external key
        /// </summary>
        /// <param name="externalKey">The external key</param>
        /// <param name="dataMappings">The list of mappings</param>
        /// <returns>The matching entry or Null if none found</returns>
        /// <remarks>If we are auto-mapping users, it will lookup the username instead</remarks>
        protected RemoteDataMapping FindUserMappingByExternalKey(string externalKey, RemoteDataMapping[] dataMappings, SoapServiceClient client)
        {
            if (this.autoMapUsers)
            {
                try
                {
                    RemoteUser remoteUser = client.User_RetrieveByUserName(externalKey, true);
                    if (remoteUser == null)
                    {
                        return null;
                    }
                    RemoteDataMapping userMapping = new RemoteDataMapping();
                    userMapping.InternalId = remoteUser.UserId.Value;
                    userMapping.ExternalKey = remoteUser.UserName;
                    return userMapping;
                }
                catch (Exception)
                {
                    //User could not be found so return null
                    return null;
                }
            }
            else
            {
                return InternalFunctions.FindMappingByExternalKey(externalKey, dataMappings);
            }
        }

        /// <summary>
        /// Logs a trace event message if the configuration option is set
        /// </summary>
        /// <param name="eventLog">The event log handle</param>
        /// <param name="message">The message to log</param>
        /// <param name="type">The type of event</param>
        protected void LogTraceEvent(EventLog eventLog, string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            if (traceLogging && eventLog != null)
            {
                if (message.Length > 31000)
                {
                    //Split into smaller lengths
                    int index = 0;
                    while (index < message.Length)
                    {
                        try
                        {
                            string messageElement = message.Substring(index, 31000);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            string messageElement = message.Substring(index);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        index += 31000;
                    }
                }
                else
                {
                    this.eventLog.WriteEntry(message, type);
                }
            }
        }

        /// <summary>
        /// Logs a trace event message if the configuration option is set
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="type">The type of event</param>
        public void LogErrorEvent(string message, EventLogEntryType type = EventLogEntryType.Error)
        {
            if (this.eventLog != null)
            {
                if (message.Length > 31000)
                {
                    //Split into smaller lengths
                    int index = 0;
                    while (index < message.Length)
                    {
                        try
                        {
                            string messageElement = message.Substring(index, 31000);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            string messageElement = message.Substring(index);
                            this.eventLog.WriteEntry(messageElement, type);
                        }
                        index += 31000;
                    }
                }
                else
                {
                    this.eventLog.WriteEntry(message, type);
                }
            }
        }

        // Implement IDisposable.
        // Do not make this method virtual.
        // A derived class should not be able to override this method.
        public void Dispose()
        {
            Dispose(true);
            // Take yourself off the Finalization queue 
            // to prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method 
        // does not get called.
        // It gives your base class the opportunity to finalize.
        // Do not provide destructors in types derived from this class.
        ~DataSync()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        // Dispose(bool disposing) executes in two distinct scenarios.
        // If disposing equals true, the method has been called directly
        // or indirectly by a user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the 
        // runtime from inside the finalizer and you should not reference 
        // other objects. Only unmanaged resources can be disposed.
        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                // If disposing equals true, dispose all managed 
                // and unmanaged resources.
                if (disposing)
                {
                    //Remove the event log reference
                    this.eventLog = null;
                }
                // Release unmanaged resources. If disposing is false, 
                // only the following code is executed.

                //This class doesn't have any unmanaged resources to worry about
            }
            disposed = true;
        }
    }
}
