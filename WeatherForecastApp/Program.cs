﻿using System;
using System.Collections.Generic;
using System.IO;
using JohnsonControls.Metasys.BasicServices;
using JohnsonControls.Metasys.BasicServices.Utils;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace WeatherForecastApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // Add support for JSON Config File
            IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("AppSettings.json", optional: false, reloadOnChange:true)
            .Build();
            // Read hostname from credential manager first
            var hostnameTarget = config.GetSection("CredentialManager:Targets:MetasysServer").Value;
            // Hostname is stored in the Credential Manager using password field
            var secureHostname = CredentialUtil.GetCredential(hostnameTarget);
            MetasysClient metasysClient = new MetasysClient(CredentialUtil.convertToUnSecureString(secureHostname.Password));
            // Retrieve Metasys Credentials to login
            var credTarget = config.GetSection("CredentialManager:Targets:MetasysCredentials").Value;
            // Use TryLogin overload that accepts Credential Manager Target
            metasysClient.TryLogin(credTarget);
            // Forecast container target is securely stored in Credential manager
            var containerTarget = config.GetSection("CredentialManager:Targets:ForecastContainer").Value;
            var secureContainer = CredentialUtil.GetCredential(containerTarget);
            // Get parent object of Weather Forecast to retrieve related children (securely stored in Credential Manager)
            Guid parentObjectId = metasysClient.GetObjectIdentifier(CredentialUtil.convertToUnSecureString(secureContainer.Password));
            IEnumerable<MetasysObject> weatherForecast = metasysClient.GetObjects(parentObjectId, 1);
            // Retrieve latitude and longitude to get weather forecast
            MetasysObject latitudePoint = weatherForecast.Where(w => w.Name == "Latitude").FirstOrDefault();
            MetasysObject longitudePoint = weatherForecast.Where(w => w.Name == "Longitude").FirstOrDefault();
            double latitude = metasysClient.ReadProperty(latitudePoint.Id, "presentValue").NumericValue;
            double longitude = metasysClient.ReadProperty(longitudePoint.Id, "presentValue").NumericValue;
            // Forecast API key is securely stored in Credential manager
            var apiKeyTarget = config.GetSection("CredentialManager:Targets:OpenWeather").Value;
            var secureApiKey = CredentialUtil.GetCredential(apiKeyTarget);
            // Get Next Five Days forecast every 3 hours           
            OpenWeatherMapClient weatherMapclient = new OpenWeatherMapClient(CredentialUtil.convertToUnSecureString(secureApiKey.Password));
            ForecastResult forecastResult = weatherMapclient.GetForecast(latitude, longitude).GetAwaiter().GetResult();
            // Get the closest forecast to be written
            Forecast forecast = forecastResult.list.First();
            // Read Metasys points to write the response back
            MetasysObject Day = weatherForecast.Where(w => w.Name == "Day").FirstOrDefault();
            MetasysObject Month = weatherForecast.Where(w => w.Name == "Month").FirstOrDefault();
            MetasysObject Year = weatherForecast.Where(w => w.Name == "Year").FirstOrDefault();
            MetasysObject Hour = weatherForecast.Where(w => w.Name == "Hour").FirstOrDefault();
            MetasysObject Minute = weatherForecast.Where(w => w.Name == "Minute").FirstOrDefault();
            MetasysObject Temperature = weatherForecast.Where(w => w.Name == "Temperature").FirstOrDefault();
            MetasysObject Humidity = weatherForecast.Where(w => w.Name == "Humidity").FirstOrDefault();
            MetasysObject Rain = weatherForecast.Where(w => w.Name == "Rain").FirstOrDefault();
            MetasysObject Snow = weatherForecast.Where(w => w.Name == "Snow").FirstOrDefault();
            // Use commands to write the results
            string adjustCommand = "Adjust";
            var date = OpenWeatherMapClient.UnixTimeStampToDateTime(forecast.dt);
            metasysClient.SendCommand(Day.Id, adjustCommand, new List<object> {date.Day});
            metasysClient.SendCommand(Month.Id, adjustCommand, new List<object> {date.Month});
            metasysClient.SendCommand(Year.Id, adjustCommand, new List<object> {date.Year});
            metasysClient.SendCommand(Hour.Id, adjustCommand, new List<object> {date.Hour});
            metasysClient.SendCommand(Minute.Id, adjustCommand, new List<object> {date.Minute});
            metasysClient.SendCommand(Temperature.Id, adjustCommand, new List<object> {forecast.main.temp});
            metasysClient.SendCommand(Humidity.Id, adjustCommand, new List<object> {forecast.main.humidity});
            if (forecast.rain != null)
            {
                metasysClient.SendCommand(Rain.Id, adjustCommand, new List<object> { forecast.rain.ThreeHours});
            }
            else
            {
                // Reset values in case there is no rain 
                metasysClient.SendCommand(Rain.Id, adjustCommand, new List<object> { 0 });
            }
            if (forecast.snow != null)
            {
                metasysClient.SendCommand(Snow.Id, adjustCommand, new List<object> { forecast.snow.ThreeHours });
            }
            else
            {
                // Reset values in case there is no snow
                metasysClient.SendCommand(Rain.Id, adjustCommand, new List<object> { 0 });
            }
        }
    }
}
