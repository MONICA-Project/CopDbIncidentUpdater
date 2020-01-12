using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Client.Receiving;
using MQTTnet.Client.Options;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Xml;
using System.IO;
using System.Threading;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;
namespace CopDbIncidentUpdater
{
    public class MQTTReciever
    {
        static
        MQTTnet.Extensions.ManagedClient.ManagedMqttClient ml = null;
        // Dictionary<string, int> ThingsFromDataStreams = new Dictionary<string, int>();
        //  Dictionary<string, int> PeopleFromDataStreams = new Dictionary<string, int>();
        // Dictionary<string, int> ZoneFromDataStreams = new Dictionary<string, int>();
        List<string> datastreams = new List<string>();
        public string incidenttype = "";
        bool useDBDirectly = true;
        HubConnection connection;
        string copApiBasePath = "";
        string copApiBasePathHub = "";
        string copAuthToken = "";
        bool usesignalR = false;
        bool useOnlySignalR = false;
        bool mqttDebug = false;
        string GOSTQuery = "";
        string OGCServerBaseURL;
        string OGCServerUID = "";
        string OGCServerPwd = "";
        string OGCMQTTPrefix = "GOST_RIF";
        string IncidentType = "CrowdIncident";
        XmlDocument xSensors = null;
        Random random = new Random();

        private readonly ILogger _logger;

        public MQTTReciever()
        {
            var logFactory = new LoggerFactory()
           .AddConsole(LogLevel.Warning)
           .AddConsole()
           .AddDebug();

            var logger = logFactory.CreateLogger<MQTTReciever>();
            _logger = logger;
        }

        public void init()
        {
            IncidentType = Environment.GetEnvironmentVariable("INCIDENT_TYPE");
            copApiBasePath = Environment.GetEnvironmentVariable("COP_API_BASE_PATH");
            copApiBasePathHub = Environment.GetEnvironmentVariable("COP_API_BASE_PATH_HUB");
            copAuthToken = Environment.GetEnvironmentVariable("COP_AUTH_TOKEN");
            string signalR = Environment.GetEnvironmentVariable("signalR");
            string OnlySignalR = Environment.GetEnvironmentVariable("useOnlySignalR");
            string smqttDebug = Environment.GetEnvironmentVariable("mqttDebug");
            GOSTQuery = Environment.GetEnvironmentVariable("GOSTQuery");
            string tmp = Environment.GetEnvironmentVariable("OGCServerBaseURL");
            if (tmp == null || tmp == "")
            {
                System.Console.WriteLine("Warning:Missing OGCServerBaseURL env variable.");
            }
            else OGCServerBaseURL = tmp;

            tmp = Environment.GetEnvironmentVariable("OGCServerUID");
            if (tmp == null || tmp == "")
            {
                System.Console.WriteLine("Warning:Missing OGCServerUID env variable.");
            }
            else OGCServerUID = tmp;

            tmp = Environment.GetEnvironmentVariable("OGCServerPwd");
            if (tmp == null || tmp == "")
            {
                System.Console.WriteLine("Warning:Missing OGCServerPwd env variable.");
            }
            else OGCServerPwd = tmp;

            tmp = Environment.GetEnvironmentVariable("OGCMQTTPrefix");
            if (tmp == null || tmp == "")
            {
                System.Console.WriteLine("Warning:Missing OGCMQTTPrefix env variable.");
            }
            else OGCMQTTPrefix = tmp;

            tmp = Environment.GetEnvironmentVariable("IncidentType");
            if (tmp == null || tmp == "")
            {
                System.Console.WriteLine("Warning:Missing IncidentType env variable.");
            }
            else IncidentType = tmp;

            if (signalR != null && signalR != "")
                usesignalR = bool.Parse(signalR);
            if (OnlySignalR != null && OnlySignalR != "")
                useOnlySignalR = bool.Parse(OnlySignalR);
            if (smqttDebug != null && smqttDebug != "")
                mqttDebug = bool.Parse(smqttDebug);


            //Find Datastreams to listen to
            XmlDocument xDoc = null;
            string JsonResult = "";
            WebClient client = new WebClient();
            try
            {
                string url = OGCServerBaseURL + "Datastreams?" + GOSTQuery;

                client.Encoding = System.Text.Encoding.UTF8;
                client.Headers["Accept"] = "application/json";
                client.Headers["Content-Type"] = "application/json";
                NetworkCredential myCreds = new NetworkCredential(OGCServerUID, OGCServerPwd);
                client.Credentials = myCreds;

                JsonResult = client.DownloadString(url);

                xDoc = JsonConvert.DeserializeXmlNode(JsonResult, "Root");

            }
            catch (WebException exception)
            {
                System.Console.WriteLine("Datastreams?$filter failed:" + exception.Message);


            }
            if (xDoc != null)
            {
                XmlNodeList foundOGCThings = xDoc.SelectNodes(".//value");
                foreach (XmlNode foundOGCThing in foundOGCThings)
                {
                    //Extract id and Observation link
                    string observationUrl = foundOGCThing.SelectSingleNode("Observations_x0040_iot.navigationLink").InnerText;



                    //Observation stream is the last part of the observationUrl http://monappdwp3.monica-cloud.eu:5050/gost_leeds/v1.0/Datastreams(3)/Observations
                    string[] parsedUrl = observationUrl.Split("/");
                    string observationTopic = OGCMQTTPrefix + "/" + parsedUrl[parsedUrl.GetUpperBound(0) - 1] + "/" + parsedUrl[parsedUrl.GetUpperBound(0)];

                    datastreams.Add(observationTopic);
                }
            }


            //Read sensor position for environment incidents
            if(IncidentType == "EnvironmentIncident")
            {
                //Fetch all datastreams that we want to handle
                string thingtype = "Windspeed";
                System.Console.WriteLine("Fetching:" + thingtype);
                string JsonThingResult = "";
                WebClient wclient = new WebClient();
                try
                {
                    wclient.Encoding = System.Text.Encoding.UTF8;
                    wclient.Headers["Accept"] = "application/json";
                    wclient.Headers["Content-Type"] = "application/json";
                    wclient.Headers["Authorization"] = copAuthToken;
                    string urn = copApiBasePath + "things?thingType=" + thingtype.Replace(" ", "%20");
                    JsonThingResult = wclient.DownloadString(urn);



                }
                catch (WebException exception)
                {
                    System.Console.WriteLine("Invokation error" + copApiBasePath + "things " + exception.Message);
                }

                string InnerText = JsonThingResult;


                // Build x-ref of thingId, personId, ZoneId mapped to topic.
                InnerText = "{Kalle:" + InnerText + "}";
                xSensors = JsonConvert.DeserializeXmlNode(InnerText, "Root");
            }
            //Start DB Connect
            IO.Swagger.DatabaseInterface.DBObservation dbO = new IO.Swagger.DatabaseInterface.DBObservation();
            string error = "";
            dbO.ListObs(2, ref error);

            //Add the data streams we listen to.

            //How to listen for fighting?
            //How to listen to Button Pressed?


            //Setup SignalR
            connection = new HubConnectionBuilder()
            .WithUrl(copApiBasePathHub + "signalR/COPUpdate")
            .Build();
            connection.StartAsync().Wait();
            // Setup and start a managed MQTT client.
            var options = new ManagedMqttClientOptionsBuilder()
                 .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                 .WithClientOptions(new MqttClientOptionsBuilder()
                     .WithClientId(System.Guid.NewGuid().ToString())
                     .WithKeepAlivePeriod(TimeSpan.FromSeconds(120))
                     .WithCommunicationTimeout(TimeSpan.FromSeconds(60))
                     //.WithCredentials("sm_user", "hemligt")
                     .WithTcpServer("192.168.229.101")
                     .Build())
                 .Build();
            ml = (MQTTnet.Extensions.ManagedClient.ManagedMqttClient)new MqttFactory().CreateManagedMqttClient();
            foreach (string topic in datastreams)
            {
                ml.SubscribeAsync(new TopicFilterBuilder().WithTopic(topic).Build());
            }
            //ml.ApplicationMessageReceived += (s, e) =>
            //{
            //    Console.WriteLine($"+ Topic = {e.ApplicationMessage.Topic}");
            //    SaveToDB(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"), e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload)); ;

            //};
            ml.ApplicationMessageReceivedHandler =
              new MqttApplicationMessageReceivedHandlerDelegate(e =>
              {
                  if (IncidentType == "SoundIncident")
                      ManageSoundIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                  if (IncidentType == "CrowdIncident")
                      ManageCrowdIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                  if (IncidentType == "QueueIncident")
                      ManageQueueIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                  if (IncidentType == "EnvironmentIncident")
                      ManageEnvironmentIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                  if (IncidentType == "OverCrowding" || IncidentType == "ObjectDetection" || IncidentType == "PeopleFlow" || IncidentType == "FightDetected")
                      ManageCameraIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));

                  //if (e.ApplicationMessage.Topic.StartsWith("/DSS/"))
                  //    ManageDSSIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                  //if (e.ApplicationMessage.Topic.StartsWith("GOST/"))
                  //    ManageDSSIncident(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));



              });
            if (mqttDebug)
            {
                MQTTnet.Diagnostics.MqttNetGlobalLogger.LogMessagePublished += (s, e) =>
                {
                    var trace = $">> [{e.TraceMessage.Timestamp:O}] [{e.TraceMessage.ThreadId}] [{e.TraceMessage.Source}] [{e.TraceMessage.Level}]: {e.TraceMessage.Message}";
                    if (e.TraceMessage.Exception != null)
                    {
                        trace += Environment.NewLine + e.TraceMessage.Exception.ToString();
                    }

                    CultureInfo ci = Thread.CurrentThread.CurrentCulture;
                    Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("sv-SE");

                    string fileName = "MQTT" + DateTime.Now.ToShortDateString() + ".txt";

                    string logFolder = "." + Path.DirectorySeparatorChar;

                    FileStream w = File.Open(logFolder + fileName, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.Write);
                    StreamWriter sw = new StreamWriter(w, System.Text.Encoding.Default);
                    sw.Write(trace + Environment.NewLine);
                    sw.Close();

                    Thread.CurrentThread.CurrentCulture = ci;
                };
            }
            ml.StartAsync(options).Wait();
        }


        public void ManageDSSIncident(string Topic, string PayLoad)
        {
            if (Topic == "/DSS/Overcrowding")
            {
                dynamic json = JValue.Parse(PayLoad);
                DateTime incidentTS = json.inputDataTimestamp;
                foreach (dynamic rec in json.recommendations)
                {
                    string errorMsg = "";
                    long newId = 0;
                    string strLat = rec.latitude.ToString();
                    string strLon = rec.longitude.ToString();
                    string recom = rec.recommendation;

                    IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
                    if (!dbI.AddIncident("Over crowded area", "OVERCROWDED", "[[" + strLat + "," + strLon + "]]", 4, "ONGOING", 78.5, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                        System.Console.WriteLine("Failed to create incident:" + errorMsg);
                    else
                    {
                        dynamic jmsg = new JObject();
                        jmsg.type = "newincident";
                        jmsg.incidentid = newId;
                        jmsg.status = "ONGOING";
                        jmsg.prio = 4;
                        jmsg.timestamp = DateTime.Now;
                        string strMessage = jmsg.ToString();
                        signalRSend("Incidents", strMessage);
                    }
                }
            }
            else if (Topic == "GOST/Datastreams(43)/Observations")
            {
                dynamic json = JValue.Parse(PayLoad);
                DateTime incidentTS = json.phenomenonTime;

                string errorMsg = "";
                long newId = 0;
                string strLat = "0.0";
                string strLon = "0.0";
                string confStr = json.result.confidence;
                double conf = double.Parse(confStr, CultureInfo.InvariantCulture);
                conf *= 100;
                string[] cameraIDs = json.result.camera_ids.ToObject<string[]>();


                IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
                if (!dbI.FindCameraCoords(cameraIDs[0], ref strLat, ref strLon))
                    return;
                if (!dbI.AddIncident("Fight Detected", "FIGHT", "[[" + strLat + "," + strLon + "]]", 4, "ONGOING", conf, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                    System.Console.WriteLine("Failed to create incident:" + errorMsg);
                else
                {
                    dynamic jmsg = new JObject();
                    jmsg.type = "newincident";
                    jmsg.incidentid = newId;
                    jmsg.status = "ONGOING";
                    jmsg.prio = 4;
                    jmsg.timestamp = DateTime.Now;
                    string strMessage = jmsg.ToString();
                    signalRSend("Incidents", strMessage);
                }

            }
        }

        public void ManageSoundIncident(string Topic, string PayLoad)
        {
            PayLoad = PayLoad.Replace("octave bands", "octavebands");
            dynamic json = JValue.Parse(PayLoad);
            DateTime incidentTS = json.phenomenonTime;

            string errorMsg = "";
            long newId = 0;
            string message = json.result.ALERT;
            if (json.result.octavebands != null)
                message += " " + json.result.octavebands;
            message += " Source:" + json.result.microphoneID;
            if (json.result.measurement_type != null)
                message += " Type:" + json.result.measurement_type;
            if (json.result.microphoneType != null)
                message += " Type:" + json.result.microphoneType;

            IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.AddIncident(message, "SoundIncident", "", 4, "ONGOING", 100, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                System.Console.WriteLine("Failed to create incident:" + errorMsg);
            else
            {
                dynamic jmsg = new JObject();
                jmsg.type = "SoundIncident";
                jmsg.incidentid = newId;
                jmsg.status = "ONGOING";
                jmsg.interventionplan = PayLoad;
                jmsg.prio = 4;
                jmsg.timestamp = DateTime.Now;
                string strMessage = jmsg.ToString();
                signalRSend("Incidents", strMessage);
            }


        }

        public void ManageCrowdIncident(string Topic, string PayLoad)
        {
            
            dynamic json = JValue.Parse(PayLoad);
            DateTime incidentTS = DateTime.Now;

            string errorMsg = "";
            long newId = 0;
            string message = json.result.description + " From:" + json.result.cameraID;
            string strLat = json.result.coordinates[1];
            string strLon = json.result.coordinates[0]; ;


            IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.AddIncident(message, "CrowdingIncident", "[[" + strLat + "," + strLon + "]]", 4, "ONGOING", 100, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                System.Console.WriteLine("Failed to create incident:" + errorMsg);
            else
            {
                dynamic jmsg = new JObject();
                jmsg.type = "newincident";
                jmsg.incidentid = newId;
                jmsg.status = "ONGOING";
                jmsg.interventionplan = PayLoad;
                jmsg.prio = 4;
                jmsg.timestamp = DateTime.Now;
                string strMessage = jmsg.ToString();
                signalRSend("Incidents", strMessage);
            }


        }

        public void ManageQueueIncident(string Topic, string PayLoad)
        {

            dynamic json = JValue.Parse(PayLoad);
            DateTime incidentTS = DateTime.Now;

            string errorMsg = "";
            long newId = 0;
            string message = json.result.description + " From:" + json.result.cameraID;
            string strLat = json.result.coordinates[1];
            string strLon = json.result.coordinates[0]; ;


            IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.AddIncident(message, "CrowdingIncident", "[[" + strLat + "," + strLon + "]]", 4, "ONGOING", 100, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                System.Console.WriteLine("Failed to create incident:" + errorMsg);
            else
            {
                dynamic jmsg = new JObject();
                jmsg.type = "newincident";
                jmsg.incidentid = newId;
                jmsg.status = "ONGOING";
                jmsg.interventionplan = PayLoad;
                jmsg.prio = 4;
                jmsg.timestamp = DateTime.Now;
                string strMessage = jmsg.ToString();
                signalRSend("Incidents", strMessage);
            }


        }

        public void ManageCameraIncident(string Topic, string PayLoad)
        {

            dynamic json = JValue.Parse(PayLoad);
            DateTime incidentTS = DateTime.Now;
            string cameraID = json.result.cameraid;
            if (cameraID == "" || cameraID == null)
                cameraID = json.result.cameraID;
            string errorMsg = "";
            long newId = 0;
            string message = json.result.type  + " " + json.result.description + " From:" + cameraID;
            string strLat = "0.0";
            string strLon = "0.0" ;
 
            IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.FindCameraCoords(cameraID, ref strLat, ref strLon))
            {
                Console.WriteLine("No matching Camera for:" + json.result.cameraID);
                return;
            }
            double latDiff = 0.0;
            double lonDiff = 0.0;
            double lat = double.Parse(strLat, CultureInfo.InvariantCulture);
            double lon = double.Parse(strLon, CultureInfo.InvariantCulture);
            CartesianToGeo(4, lat, lon, ref latDiff, ref lonDiff);
            int randLon = random.Next(-3, 3);
            int randLat = random.Next(-3, 3);

            lat += ((double) randLon) * latDiff;
            lon += ((double) randLat) * lonDiff;

            dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.AddIncident(message, IncidentType, "[[" + lat.ToString(CultureInfo.InvariantCulture) + "," + lon.ToString(CultureInfo.InvariantCulture) + "]]", 4, "ONGOING", 100, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                System.Console.WriteLine("Failed to create incident:" + errorMsg);
            else
            {
                dynamic jmsg = new JObject();
                jmsg.type = "newincident";
                jmsg.incidentid = newId;
                jmsg.status = "ONGOING";
                jmsg.interventionplan = PayLoad;
                jmsg.prio = 4;
                jmsg.timestamp = DateTime.Now;
                string strMessage = jmsg.ToString();
                signalRSend("Incidents", strMessage);
            }


        }

        public void ManageEnvironmentIncident(string Topic, string PayLoad)
        {

            dynamic json = JValue.Parse(PayLoad);
            DateTime incidentTS = DateTime.Now;

            string errorMsg = "";
            long newId = 0;
            string message = json.result.description ;
            string strLat = "0.0";
            string strLon = "0.0";

            XmlNode xLat = xSensors.SelectSingleNode("//Kalle[ogcid='"+json.result.name+"']/lat");
            if (xLat != null)
                strLat = xLat.InnerText;

            XmlNode xLon = xSensors.SelectSingleNode("//Kalle[ogcid='" + json.result.name + "']/lon");
            if (xLon != null)
                strLon = xLon.InnerText;

            IO.Swagger.DatabaseInterface.DBIncident dbI = new IO.Swagger.DatabaseInterface.DBIncident();
            if (!dbI.AddIncident(message, "Wind", "[[" + strLat + "," + strLon + "]]", 4, "ONGOING", 100, PayLoad, incidentTS, "", "", "", "", "", ref errorMsg, ref newId))
                System.Console.WriteLine("Failed to create incident:" + errorMsg);
            else
            {
                dynamic jmsg = new JObject();
                jmsg.type = "newincident";
                jmsg.incidentid = newId;
                jmsg.status = "ONGOING";
                jmsg.interventionplan = PayLoad;
                jmsg.prio = 4;
                jmsg.timestamp = DateTime.Now;
                string strMessage = jmsg.ToString();
                signalRSend("Incidents", strMessage);
            }


        }

        public void signalRSend(string action, string message)
        {
            string payload = @"{
   ""action"": ""###action###"",
   ""message"": ###message###
 }";
            payload = payload.Replace("###action###", action);
            payload = payload.Replace("###message###", JsonConvert.ToString(message));


            if (action != "")
            {
                WebClient client = new WebClient();
                try
                {
                    client.Encoding = System.Text.Encoding.UTF8;
                    client.Headers["Accept"] = "application/json";
                    client.Headers["Content-Type"] = "application/json";
                    string JsonResult = client.UploadString(copApiBasePath + "HUBMessage", payload);

                }
                catch (WebException exception)
                {
                    System.Console.WriteLine("Invokation error" + copApiBasePath + "HUBMessage" + exception.Message);
                }
            }
        }

        private void CartesianToGeo(int stepMeters, double lat, double lon, ref double latDiff, ref double lonDiff)
        {
            //Earth’s radius, sphere
            double R = 6378137.0;

            //offsets in meters
            double dn = stepMeters;
            double de = stepMeters;

            //Coordinate offsets in radians
            double dLat = dn / R;
            double dLon = de / (R * Math.Cos(Math.PI * lat / 180));

            //OffsetPosition, decimal degrees
            double latO = lat + dLat * 180.0 / Math.PI;
            double lonO = lon + dLon * 180.0 / Math.PI;

            latDiff = dLat * 180.0 / Math.PI;
            lonDiff = dLon * 180.0 / Math.PI;
        }




    }
}

