using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CopDbIncidentUpdater
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string connstr = Environment.GetEnvironmentVariable("CONNECTION_STR");
            if (connstr == null || connstr == "")
            {
                System.Console.WriteLine("ERROR:Missing CONNECTION_STR env variable. Process Exit.");
            }
            else settings.ConnectionString = connstr;

            MQTTReciever mqttr = new MQTTReciever();
            mqttr.init();
            CreateWebHostBuilder(args).Build().Run();

            while (true)
                System.Threading.Thread.Sleep(500);
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();
    }
}
