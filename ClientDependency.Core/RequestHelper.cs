using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using ClientDependency.Core.Config;

namespace ClientDependency.Core
{
    /// <summary>
    /// A utility class for getting the string result from an URL resource
    /// </summary>
    internal class RequestHelper
    {
        private static readonly string ByteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());

        /// <summary>
        /// Tries to convert the url to a uri, then read the request into a string and return it.
        /// This takes into account relative vs absolute URI's
        /// </summary>
        /// <param name="url"></param>
        /// <param name="approvedDomains">a list of domains approved to make requests to in order to get a response</param>
        /// <param name="requestContents"></param>
        /// <param name="http"></param>
        /// <param name="resultUri">
        /// The Uri that was used to get the result. Depending on the extension this may be absolute and might not be. 
        /// If it is an aspx request, then it will be relative.
        /// </param>
        /// <returns>true if successful, false if not successful</returns>
        /// <remarks>
        /// if the path is a relative local path, the we use Server.Execute to get the request output, otherwise
        /// if it is an absolute path, a WebClient request is made to fetch the contents.
        /// </remarks>
        internal static bool TryReadUri(
            string url, 
            HttpContextBase http, 
            IEnumerable<string> approvedDomains, 
            out string requestContents,
            out Uri resultUri)
        {
            ClientDependencySettings.Instance.Logger.Debug(string.Format("Trying to read from URI : {0}", url));

            Uri uri;
            if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out uri))
            {
                //flag of whether or not to make a request to get the external resource (used below)
                var bundleExternalUri = false;

                //if its a relative path, then check if we should execute/retreive contents,
                //otherwise change it to an absolute path and try to request it.
                if (!uri.IsAbsoluteUri)
                {
                    //if this is an ASPX page, we should execute it instead of http getting it.
                    if (uri.ToString().EndsWith(".aspx", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var sw = new StringWriter();
                        try
                        {
                            http.Server.Execute(url, sw);
                            requestContents = sw.ToString();
                            sw.Close();
                            resultUri = uri;
                            return true;
                        }
                        catch (Exception ex)
                        {
                            ClientDependencySettings.Instance.Logger.Error(string.Format("Could not load file contents from {0}. EXCEPTION: {1}", url, ex.Message), ex);
                            requestContents = "";
                            resultUri = null;
                            return false;
                        }
                    }

                    //if this is a call for a web resource, we should http get it
                    if (url.StartsWith(http.Request.ApplicationPath.TrimEnd('/') + "/webresource.axd", StringComparison.InvariantCultureIgnoreCase))
                    {
                        bundleExternalUri = true;
                    }                                       
                }

                try
                {
                    //we've gotten this far, make the URI absolute and try to load it
                    uri = uri.MakeAbsoluteUri(http);

                    //if this isn't a web resource, we need to check if its approved
                    if (!bundleExternalUri)
                    {
                        //first, we will just allow local requests
                        if (uri.IsLocalUri(http))
                        {
                            bundleExternalUri = true;
                        }
                        else
                        {
                            // get the domain to test, with starting dot and trailing port, then compare with
                            // declared (authorized) domains. the starting dot is here to allow for subdomain
                            // approval, eg '.maps.google.com:80' will be approved by rule '.google.com:80', yet
                            // '.roguegoogle.com:80' will not.
                            var domain = string.Format(".{0}:{1}", uri.Host, uri.Port);

                            if (approvedDomains.Any(bundleDomain => domain.EndsWith(bundleDomain)))
                            {
                                bundleExternalUri = true;
                            }
                        }
                    }

                    if (bundleExternalUri)
                    {
                        ClientDependencySettings.Instance.Logger.Debug(string.Format("Calling GetXmlResponse with URI : {0}", uri.AbsoluteUri));
                        requestContents = GetXmlResponse(uri);
                        resultUri = uri;
                        return true;
                    }

                    ClientDependencySettings.Instance.Logger.Error(string.Format("Could not load file contents from {0}. Domain is not white-listed.", url), null);
                }
                catch (Exception ex)
                {
                    ClientDependencySettings.Instance.Logger.Error(string.Format("Could not load file contents from {0}. EXCEPTION: {1}", url, ex.Message), ex);
                }


            }
            requestContents = "";
            resultUri = null;
            return false;
        }

        /// <summary>
        /// Gets the web response and ensures that the BOM is not present not matter what encoding is specified.
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        internal static string GetXmlResponse(Uri resource)
        {
            string xml;

            using (var client = new CookieAwareWebClient())
            {
                client.Credentials = CredentialCache.DefaultNetworkCredentials;
                client.Encoding = Encoding.UTF8;
                if (HttpContext.Current != null)
                {
                    client.AddHttpCookieCollection(HttpContext.Current.Request.Url.Host, HttpContext.Current.Request.Cookies);    
                }

                xml = client.DownloadString(resource);
            }

            if (xml.StartsWith(ByteOrderMarkUtf8))
            {
                xml = xml.Remove(0, ByteOrderMarkUtf8.Length - 1);
            }

            return xml;
        }

        /// <summary>
        /// The cookie aware web client.
        /// </summary>
        public class CookieAwareWebClient : WebClient
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="CookieAwareWebClient"/> class.
            /// </summary>
            public CookieAwareWebClient()
            {
                CookieContainer = new CookieContainer();
            }

            /// <summary>
            /// Gets the cookie container.
            /// </summary>
            public CookieContainer CookieContainer { get; private set; }

            /// <summary>
            /// The add http cookie collection.
            /// </summary>
            /// <param name="domain">
            /// The domain.
            /// </param>
            /// <param name="httpCookieCollection">
            /// The http cookie collection.
            /// </param>
            public void AddHttpCookieCollection(string domain, HttpCookieCollection httpCookieCollection)
            {
                foreach (string httpCookieName in httpCookieCollection)
                {
                    var httpCookie = httpCookieCollection[httpCookieName];

                    if (httpCookie != null)
                    {
                        // Convert between the System.Net.Cookie to a System.Web.HttpCookie
                        var cookie = new Cookie
                        {
                            Domain = domain,
                            Expires = httpCookie.Expires,
                            Name = httpCookie.Name,
                            Path = httpCookie.Path,
                            Secure = httpCookie.Secure,
                            Value = httpCookie.Value
                        };

                        CookieContainer.Add(cookie);
                    }
                }
            }

            /// <summary>
            /// The get web request.
            /// </summary>
            /// <param name="address">
            /// The address.
            /// </param>
            /// <returns>
            /// The <see cref="WebRequest"/>.
            /// </returns>
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address) as HttpWebRequest;
                if (request != null)
                {
                    request.CookieContainer = CookieContainer;
                    return request;
                }

                return base.GetWebRequest(address);
            }
        }
    }
}
