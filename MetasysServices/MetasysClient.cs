﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using JohnsonControls.Metasys.BasicServices.Interfaces;
using JohnsonControls.Metasys.BasicServices.Models;
using System.Resources;
using System.Reflection;
using System.Threading;
using System.Net;

namespace JohnsonControls.Metasys.BasicServices
{
    public class MetasysClient : IMetasysClient
    {
        protected FlurlClient Client;

        protected AccessToken AccessToken;

        protected bool RefreshToken;

        protected const int MAX_PAGE_SIZE = 1000;

        // Init Resource Manager to provide translations
        protected static ResourceManager Resource =
            new ResourceManager("JohnsonControls.Metasys.BasicServices.Resources.MetasysResources", typeof(MetasysClient).Assembly);

        /// <summary>
        /// The current Culture Used for Metasys client localization
        /// </summary>
        public CultureInfo Culture { get; set; }

        /// <summary>
        /// Creates a new TraditionalClient.
        /// </summary>
        /// <remarks>
        /// Takes an optional CultureInfo which is useful for formatting numbers and localization of strings. If not specified,
        /// the user's current culture is used.
        /// </remarks>
        /// <param name="hostname"></param>
        /// <param name="ignoreCertificateErrors"></param>
        /// <param name="version"></param>
        /// <param name="cultureInfo"></param>
        public MetasysClient(string hostname, bool ignoreCertificateErrors = false, ApiVersion version = ApiVersion.V2, CultureInfo cultureInfo = null)
        {
            // Set Metasys client if specified, otherwise use Current Culture
            Culture = cultureInfo ?? CultureInfo.CurrentCulture;

            // Init HTTP client
            AccessToken = new AccessToken(null, DateTime.UtcNow);
            FlurlHttp.Configure(settings => settings.OnErrorAsync = HandleFlurlErrorAsync);

            if (ignoreCertificateErrors)
            {
                HttpClientHandler httpClientHandler = new HttpClientHandler();
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                HttpClient httpClient = new HttpClient(httpClientHandler);
                httpClient.BaseAddress = new Uri($"https://{hostname}"
                    .AppendPathSegments("api", version));
                Client = new FlurlClient(httpClient);
            }
            else
            {
                Client = new FlurlClient($"https://{hostname}"
                    .AppendPathSegments("api", version));
            }
        }

        /// <summary>
        /// Returns localized string for the current Metasys client locale or specified culture as optional parameters.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="cultureInfo"></param>
        /// <returns></returns>
        public string Localize(string resource, CultureInfo cultureInfo = null)
        {
            return StaticLocalize(resource, cultureInfo ?? Culture);
        }

        /// <summary>
        /// Returns localized string for the specified culture or fallbacks to en-US localization.
        /// Static method for exposure to outside classes.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="cultureInfo"></param>
        /// <returns></returns>
        public static string StaticLocalize(string resource, CultureInfo cultureInfo)
        {
            try
            {
                // Priority is the cultureInfo  parameter if available, otherwise Metasys client culture
                return Resource.GetString(resource, cultureInfo);
            }
            catch (MissingManifestResourceException)
            {
                try
                {
                    // Fallback to en-US language if no resource found
                    return Resource.GetString(resource, new CultureInfo(1033));
                }
                catch (MissingManifestResourceException)
                {
                    // Just return resource placeholder when no translation found
                    return resource;
                }
            }
        }

        /// <summary>
        /// Throws MetasysExceptions when a Flurl exception occurs
        /// </summary>
        /// <returns>
        /// <exception cref="MetasysHttpParsingException"></exception>
        /// <exception cref="MetasysHttpTimeoutException"></exception>
        /// <exception cref="MetasysHttpException"></exception>
        private async Task HandleFlurlErrorAsync(HttpCall call)
        {
            if (call.Exception.GetType() == typeof(Flurl.Http.FlurlParsingException))
            {
                throw new MetasysHttpParsingException(call, "JSON", call.Response.Content.ToString(), call.Exception.InnerException);
            }
            else if (call.Exception.GetType() == typeof(Flurl.Http.FlurlHttpTimeoutException))
            {
                throw new MetasysHttpTimeoutException(call, call.Exception.InnerException);
            }
            else
            {
                throw new MetasysHttpException(call, call.Exception.Message, call.Response.Content.ToString(), call.Exception.InnerException);
            }
        }

        private async Task LogErrorAsync(String message)
        {
            await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts to login to the given host.
        /// </summary>
        public AccessToken TryLogin(string username, string password, bool refresh = true)
        {
            return TryLoginAsync(username, password, refresh).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Attempts to login to the given host asynchronously.
        /// </summary>
        /// <returns>
        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>
        public async Task<AccessToken> TryLoginAsync(string username, string password, bool refresh = true)
        {
            this.RefreshToken = refresh;

            var response = await Client.Request("login")
                .PostJsonAsync(new { username, password })
                .ReceiveJson<JToken>()
                .ConfigureAwait(false);

            CreateAccessToken(response);
            return this.AccessToken;
        }

        /// <summary>
        /// Requests a new access token before current token expires.
        /// </summary>
        public AccessToken Refresh()
        {
            return RefreshAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Requests a new access token before current token expires asynchronously.
        /// </summary>
        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>
        public async Task<AccessToken> RefreshAsync()
        {
            var response = await Client.Request("refreshToken")
                .GetJsonAsync<JToken>()
                .ConfigureAwait(false);

            CreateAccessToken(response);
            return this.AccessToken;
        }

        /// <summary>
        /// Creates a new AccessToken from a JToken and sets the client's authorization header if successful.
        /// On failure sets the AccessToken value to null and leaves authorization header in previous state.
        /// </summary>
        /// <exception cref="MetasysTokenException"></exception>
        private void CreateAccessToken(JToken token)
        {
            try
            {
                var accessTokenValue = token["accessToken"];
                var expires = token["expires"];
                var accessToken = $"Bearer {accessTokenValue.Value<string>()}";
                var date = expires.Value<DateTime>();
                this.AccessToken = new AccessToken(accessToken, date);
                Client.Headers.Remove("Authorization");
                Client.Headers.Add("Authorization", this.AccessToken.Token);
                if (RefreshToken)
                {
                    ScheduleRefresh();
                }
            }
            catch (System.NullReferenceException)
            {
                throw new MetasysTokenException(token.ToString());
            }
        }

        /// <summary>
        /// Will call Refresh() a minute before the token expires.
        /// </summary>
        private void ScheduleRefresh()
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan delay = AccessToken.Expires - now;
            delay.Subtract(new TimeSpan(0, 1, 0));

            if (delay <= TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            int delayms = (int)delay.TotalMilliseconds;

            // If the time in milliseconds is greater than max int delayms will be negative and will not schedule a refresh.
            if (delayms >= 0)
            {
                System.Threading.Tasks.Task.Delay(delayms).ContinueWith(_ => Refresh());
            }
        }

        /// <summary>
        /// Returns the current access token and it's expiration date.
        /// </summary>
        public AccessToken GetAccessToken()
        {
            return this.AccessToken;
        }

        /// <summary>
        /// Returns the object identifier (id) of the specified object.
        /// </summary>
        public Guid GetObjectIdentifier(string itemReference)
        {
            return GetObjectIdentifierAsync(itemReference).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns the object identifier (id) of the specified object asynchronously.
        /// </summary>
        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>
        /// <exception cref="MetasysGuidException"></exception>
        public async Task<Guid> GetObjectIdentifierAsync(string itemReference)
        {
            var response = await Client.Request("objectIdentifiers")
                .SetQueryParam("fqr", itemReference)
                .GetJsonAsync<JToken>()
                .ConfigureAwait(false);

            string str = null;
            try
            {
                str = response.Value<string>();
                var id = new Guid(str);
                return id;
            }
            catch (System.ArgumentNullException)
            {
                throw new MetasysGuidException("Argument Null");
            }
            catch (System.ArgumentException)
            {
                throw new MetasysGuidException("Bad Argument", str);
            }
        }

        /// <summary>
        /// Read one attribute value given the Guid of the object.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="attributeName"></param>
        public Variant ReadProperty(Guid id, string attributeName, bool throwsNotFoundException = true)
        {
            return ReadPropertyAsync(id, attributeName, throwsNotFoundException).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read one attribute value given the Guid of the object asynchronously.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="attributeName"></param>
        /// <param name="throwsNotFoundException"></param> 
        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>
        /// <exception cref="System.NullReferenceException"></exception>
        public async Task<Variant> ReadPropertyAsync(Guid id, string attributeName, bool throwsNotFoundException=true)
        {
            JToken response=null;
            try
            {
                response = await Client.Request(new Url("objects")
                .AppendPathSegments(id, "attributes", attributeName))
                .GetJsonAsync<JToken>()
                .ConfigureAwait(false);           
                var attribute = response["item"][attributeName];
                return new Variant(id, attribute, attributeName, Culture);
            }
            catch (FlurlHttpException ex)
            {
                if (ex.Call.Response.StatusCode== HttpStatusCode.NotFound && !throwsNotFoundException)
                {
                    // When specified just return an unknown value when object is not found, for all other responses an exception is raised
                    return new Variant();
                }                
                throw new MetasysPropertyException(response.ToString());               
            }
        }

        /// <summary>
        /// Read many attribute values given the Guids of the objects.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="attributeNames"></param>
        /// <param name="throwsNotFoundException"></param>
        public IEnumerable<VariantMultiple> ReadPropertyMultiple(IEnumerable<Guid> ids,
            IEnumerable<string> attributeNames, bool throwsNotFoundException = false)
        {
            return ReadPropertyMultipleAsync(ids, attributeNames,throwsNotFoundException).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Read many attribute values given the Guids of the objects asynchronously.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="attributeNames"></param>
        /// <param name="throwsNotFoundException"></param>
        public async Task<IEnumerable<VariantMultiple>> ReadPropertyMultipleAsync(IEnumerable<Guid> ids,
            IEnumerable<string> attributeNames, bool throwsNotFoundException = false)
        {
            if (ids == null || attributeNames == null)
            {
                return null;
            }
            List<VariantMultiple> results = new List<VariantMultiple>();
            var taskList = new List<Task<Variant>>();
            // Prepare Tasks to Read attributes list. In Metasys 11 this will be implemented server side
            foreach (var id in ids)
            {
                foreach (string attributeName in attributeNames)
                {
                    // Much faster reading single property than the entire object, even though we have more server calls
                    taskList.Add(ReadPropertyAsync(id, attributeName, throwsNotFoundException)); // Suppress not found exception for read property multiple
                }
            }                        
            await Task.WhenAll(taskList).ConfigureAwait(false);                       
            foreach (var id in ids)
            {
                // Get attributes of the specific Id
                List<Task<Variant>> attributeList = taskList.Where(w => w.Result.Id == id).ToList();
                List<Variant> variants = new List<Variant>();
                foreach (var t in attributeList)
                {
                    if (t.Result.Id != Guid.Empty) // Something went wrong if the result is unknown
                    {
                        variants.Add(t.Result); // Prepare variants list
                    }
                }
                if (variants.Count > 0 || attributeNames.Count()==0) 
                {
                    // Aggregate results only when objects was found or no attributes specified
                    results.Add(new VariantMultiple(id, variants));
                }
            }
            return results.AsEnumerable();
        }

        /// <summary>
        /// Read entire object given the Guid of the object asynchronously.
        /// </summary>
        /// <param name="id"></param>
        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>
        private async Task<(Guid Id, JToken Token)> ReadObjectAsync(Guid id)
        {
            var response = await Client.Request(new Url("objects")
                .AppendPathSegment(id))
                .GetJsonAsync<JToken>()
                .ConfigureAwait(false);
            return (Id: id, Token: response);
        }

        /// <summary>

        /// Write a single attribute given the Guid of the object.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="attributeName"></param>

        /// <param name="newValue"></param>

        /// <param name="priority"></param>

        public void WriteProperty(Guid id, string attributeName, object newValue, string priority = null)

        {

            WritePropertyAsync(id, attributeName, newValue, priority).GetAwaiter().GetResult();

        }



        /// <summary>

        /// Write a single attribute given the Guid of the object asynchronously.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="attributeName"></param>

        /// <param name="newValue"></param>

        /// <param name="priority"></param>

        public async Task WritePropertyAsync(Guid id, string attributeName, object newValue, string priority = null)

        {

            List<(string Attribute, object Value)> list = new List<(string Attribute, object Value)>();

            list.Add((Attribute: attributeName, Value: newValue));

            var item = GetWritePropertyBody(list, priority);



            await WritePropertyRequestAsync(id, item).ConfigureAwait(false);

        }



        /// <summary>

        /// Write to all attributes given the Guids of the objects.

        /// </summary>

        /// <param name="ids"></param>

        /// <param name="attributeValues">The (attribute, value) pairs</param>

        /// <param name="priority"></param>

        public void WritePropertyMultiple(IEnumerable<Guid> ids,

            IEnumerable<(string Attribute, object Value)> attributeValues, string priority = null)

        {

            WritePropertyMultipleAsync(ids, attributeValues, priority).GetAwaiter().GetResult();

        }



        /// <summary>

        /// Write many attribute values given the Guids of the objects asynchronously.

        /// </summary>

        /// <param name="ids"></param>

        /// <param name="attributeValues">The (attribute, value) pairs</param>

        /// <param name="priority"></param>

        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>

        public async Task WritePropertyMultipleAsync(IEnumerable<Guid> ids,

            IEnumerable<(string Attribute, object Value)> attributeValues, string priority = null)

        {

            if (ids == null || attributeValues == null)

            {

                return;

            }



            var item = GetWritePropertyBody(attributeValues, priority);



            var taskList = new List<Task>();



            foreach (var id in ids)

            {

                taskList.Add(WritePropertyRequestAsync(id, item));

            }



            await Task.WhenAll(taskList).ConfigureAwait(false);

        }



        /// <summary>

        /// Creates the body for the WriteProperty and WritePropertyMultiple requests.

        /// </summary>

        /// <param name="attributeValues">The (attribute, value) pairs</param>

        /// <param name="priority"></param>

        private Dictionary<string, object> GetWritePropertyBody(

            IEnumerable<(string Attribute, object Value)> attributeValues, string priority)

        {

            Dictionary<string, object> pairs = new Dictionary<string, object>();

            foreach (var attribute in attributeValues)

            {

                pairs.Add(attribute.Attribute, attribute.Value);

            }



            if (priority != null)

            {

                pairs.Add("priority", priority);

            }



            return pairs;

        }



        /// <summary>

        /// Write one or many attribute values in the provided json given the Guid of the object asynchronously.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="body"></param>

        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>

        private async Task WritePropertyRequestAsync(Guid id, Dictionary<string, object> body)

        {

            var json = new { item = body };

            var response = await Client.Request(new Url("objects")

                .AppendPathSegment(id))

                .PatchJsonAsync(json)

                .ConfigureAwait(false);

        }



        /// <summary>

        /// Get all available commands given the Guid of the object.

        /// </summary>

        /// <param name="id"></param>

        public IEnumerable<Command> GetCommands(Guid id)

        {

            return GetCommandsAsync(id).GetAwaiter().GetResult();

        }



        /// <summary>

        /// Get all available commands given the Guid of the object asynchronously.

        /// </summary>

        /// <param name="id"></param>

        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>

        public async Task<IEnumerable<Command>> GetCommandsAsync(Guid id)

        {

            var token = await Client.Request(new Url("objects")

                .AppendPathSegments(id, "commands"))

                .GetJsonAsync<JToken>()

                .ConfigureAwait(false);



            List<Command> commands = new List<Command>();



            var array = token as JArray;



            if (array != null)

            {

                foreach (JObject command in array)

                {

                    Command c = new Command(command);

                    commands.Add(c);

                }

            }

            else

            {

                await LogErrorAsync("Could not parse response data.").ConfigureAwait(false);

            }

            return commands;

        }



        /// <summary>

        /// Send a command to an object.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="command"></param>

        /// <param name="values"></param>

        public void SendCommand(Guid id, string command, IEnumerable<object> values = null)

        {

            SendCommandAsync(id, command, values).GetAwaiter().GetResult();

        }



        /// <summary>

        /// Send a command to an object asynchronously.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="command"></param>

        /// <param name="values"></param>

        public async Task SendCommandAsync(Guid id, string command, IEnumerable<object> values = null)

        {

            if (values == null)

            {

                await SendCommandRequestAsync(id, command, new string[0]).ConfigureAwait(false);

            }

            else

            {

                await SendCommandRequestAsync(id, command, values).ConfigureAwait(false);

            }

        }



        /// <summary>

        /// Send a command to an object asynchronously.

        /// </summary>

        /// <param name="id"></param>

        /// <param name="command"></param>

        /// <param name="json">The command body</param>

        /// <exception cref="Flurl.Http.FlurlHttpException"></exception>

        private async Task SendCommandRequestAsync(Guid id, string command, IEnumerable<object> values)
        {

            var response = await Client.Request(new Url("objects")

                .AppendPathSegments(id, "commands", command))

                .PutJsonAsync(values)

                .ConfigureAwait(false);

        }
      
    }
}
