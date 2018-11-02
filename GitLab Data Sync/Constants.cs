using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GitLabDataSync
{
    /// <summary>
    /// Stores the constants used by the DataSync class
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// The path to the Spira web service relative to the application's base URL
        /// </summary>
        public const string WEB_SERVICE_URL_SUFFIX = "/Services/v5_0/SoapService.svc";

        //Spira artifact prefixes
        public const string INCIDENT_PREFIX = "IN";
        public const string REQUIREMENT_PREFIX = "RQ";
        public const string TEST_RUN_PREFIX = "TR";

        public const int INCIDENT_PAGE_SIZE = 100;

        #region Enumerations

        /// <summary>
        /// The artifact types used in the data-sync
        /// </summary>
        public enum ArtifactType
        {
            Incident = 3,
            Release = 4
        }

        /// <summary>
        /// The artifact field ids used in the data-sync
        /// </summary>
        public enum ArtifactField
        {
            Severity = 1,
            Priority = 2,
            Status = 3,
            Type = 4
        }

        /// <summary>
        /// The various custom property types
        /// </summary>
        public enum CustomPropertyType
        {
            Text = 1,
            Integer = 2,
            Decimal = 3,
            Boolean = 4,
            Date = 5,
            List = 6,
            MultiList = 7,
            User = 8
        }

        /// <summary>
        /// The various custom property options
        /// </summary>
        public enum CustomPropertyOption
        {
            AllowEmpty = 1,
            MaxLength = 2,
            MinLength = 3,
            RichText = 4,
            Default = 5,
            MaxValue = 6,
            MinValue = 7,
            Precision = 8
        }

        /// <summary>
        /// The different types of attachment
        /// </summary>
        public enum AttachmentType
        {
            File = 1,
            URL = 2
        }

        #endregion
    }
}
