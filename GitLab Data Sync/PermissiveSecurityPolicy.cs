using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GitLabDataSync
{
    /// <summary>
    /// Allows the use of Self-Signed SSL certificates with the data-sync
    /// </summary>
    public class PermissiveCertificatePolicy
    {
        string subjectName = "";
        static PermissiveCertificatePolicy currentPolicy;

        PermissiveCertificatePolicy(string subjectName)
        {
            this.subjectName = subjectName;
            ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(RemoteCertValidate);
        }
        
        public static void Enact(string subjectName)
        {
            currentPolicy = new PermissiveCertificatePolicy(subjectName);
        }



        bool RemoteCertValidate(object sender, X509Certificate cert, X509Chain chain, System.Net.Security.SslPolicyErrors error)
        {
            if (cert.Subject == subjectName || subjectName == "")
            {
                return true;
            }

            return false;
        }
    }
}
