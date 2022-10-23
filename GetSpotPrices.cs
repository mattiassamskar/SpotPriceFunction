using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;
using NodaTime.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace SpotPrices
{
  public static class GetSpotPrices
  {
    [FunctionName("GetSpotPrices")]
    public static async Task<HttpResponseMessage> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
    {
      log.LogInformation("GetPrices function triggered. Hydrating cache");
      var clock = SystemClock.Instance.InZone(DateTimeZoneProviders.Tzdb["Europe/Stockholm"]);
      log.LogInformation("Clock is " + clock.GetCurrentLocalDateTime());
      var today = clock.GetCurrentDate();
      await Cache.Hydrate(today);
      Cache.StoreToday(today);
      var tomorrow = today.PlusDays(1);
      Cache.StoreTommorow(tomorrow);
      log.LogInformation("Today is " + today + " and tomorrow is " + tomorrow);

      var exchangeRate = await GetExchangeRate(today, log);
      Cache.StoreRate(exchangeRate);
      var todayPricePoints = await GetPricePoints(today, log);
      Cache.StoreTodayPrices(todayPricePoints);
      var tomorrowPricePoints = await GetPricePoints(tomorrow, log);
      Cache.StoreTomorrowPrices(tomorrowPricePoints);
      var todayPrices = ConvertToSekPerKwh(todayPricePoints, exchangeRate);
      var tomorrowPrices = ConvertToSekPerKwh(tomorrowPricePoints, exchangeRate);
      await Cache.PersistCache();

      var json = JsonConvert.SerializeObject(new
      {
        rate = exchangeRate,
        today,
        tomorrow,
        todayPrices = todayPrices.Select(price => price.Amount),
        tomorrowPrices = tomorrowPrices.Select(price => price.Amount)
      });

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json)
      };
    }

    static IEnumerable<PricePoint> ConvertToSekPerKwh(IEnumerable<PricePoint> pricePoints, double exchangeRate)
    {
      return pricePoints.Select(pricePoint => new PricePoint { Amount = Math.Round(pricePoint.Amount * exchangeRate) / 10 });
    }

    static async Task<double> GetExchangeRate(LocalDate localDate, ILogger log)
    {
      if (Cache.PriceInfo.Rate != 0)
      {
        return Cache.PriceInfo.Rate;
      }
      var defaultExchangeRate = System.Environment.GetEnvironmentVariable("DefaultExchangeRate");
      try
      {
        var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = $"https://api.apilayer.com/exchangerates_data/v1/{date}?base=EUR&symbols=SEK";
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequestMessage.Headers.Add("apikey", System.Environment.GetEnvironmentVariable("ApiLayer_ApiKey"));

        var response = await new HttpClient().SendAsync(httpRequestMessage);

        if (!response.IsSuccessStatusCode)
        {
          log.LogError("Could not fetch exhange rate");
          return double.Parse(defaultExchangeRate);
        }

        var result = await response.Content.ReadAsStringAsync();
        var jObject = JObject.Parse(result);
        return jObject["rates"]["SEK"].Value<double>();
      }
      catch
      {
        log.LogError("Could not parse exchange rate");
        return double.Parse(defaultExchangeRate);
      }
    }

    static async Task<IEnumerable<PricePoint>> GetPricePoints(LocalDate localDate, ILogger log)
    {
      if (Cache.PriceInfo.Today == localDate && Cache.PriceInfo.TodayPrices.Count > 0)
      {
        return Cache.PriceInfo.TodayPrices;
      }
      if (Cache.PriceInfo.Tomorrow == localDate && Cache.PriceInfo.TomorrowPrices.Count > 0)
      {
        return Cache.PriceInfo.TomorrowPrices;
      }

      try
      {
        var securityToken = System.Environment.GetEnvironmentVariable("Entsoe_SecurityToken");
        var period = localDate.PlusDays(-1).ToString("yyyyMMdd2300", CultureInfo.InvariantCulture);
        var url = $"https://web-api.tp.entsoe.eu/api?documentType=A44&in_Domain=10Y1001A1001A46L&out_Domain=10Y1001A1001A46L&periodStart={period}&periodEnd={period}&securityToken={securityToken}";

        HttpResponseMessage response = await new HttpClient().GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
          log.LogError("Could not fetch price points");
          return new List<PricePoint>();
        }
        var result = await response.Content.ReadAsStringAsync();

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(result);
        var jObject = JObject.Parse(JsonConvert.SerializeXmlNode(doc));
        IList<JToken> results = jObject["Publication_MarketDocument"]["TimeSeries"]["Period"]["Point"].Children().ToList();

        return results.Select(result => result.ToObject<PricePoint>());
      }
      catch
      {
        log.LogError("Error while parsing price points");
        return new List<PricePoint>();
      }
    }
  }
}
