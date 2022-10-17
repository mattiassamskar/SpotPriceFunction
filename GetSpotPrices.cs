using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NodaTime;
using NodaTime.Extensions;
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
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
      log.LogInformation("GetPrices function triggered.");

      var clock = SystemClock.Instance.InZone(DateTimeZoneProviders.Tzdb["Europe/Stockholm"]);
      var today = clock.GetCurrentDate();
      var tomorrow = today.PlusDays(1);

      var exchangeRate = await GetExchangeRate(today);
      var todayPricePoints = await GetPricePoints(today);
      var tomorrowPricePoints = await GetPricePoints(today);
      var todayPrices = ConvertToSekPerKwh(todayPricePoints, exchangeRate);
      var tomorrowPrices = ConvertToSekPerKwh(tomorrowPricePoints, exchangeRate);

      var json = JsonConvert.SerializeObject(new
      {
        exchangeRate,
        todayPrices,
        tomorrowPrices
      });

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(json)
      };
    }

    static IEnumerable<PricePoint> ConvertToSekPerKwh(IEnumerable<PricePoint> pricePoints, double exchangeRate)
    {
      return pricePoints.Select(pricePoint => new PricePoint { Amount = pricePoint.Amount * exchangeRate });
    }

    static async Task<double> GetExchangeRate(LocalDate localDate)
    {
      var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      var url = $"https://api.apilayer.com/exchangerates_data/v1/{date}?base=EUR&symbols=SEK";
      var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, url);
      httpRequestMessage.Headers.Add("apikey", System.Environment.GetEnvironmentVariable("ApiKey"));

      var response = await new HttpClient().SendAsync(httpRequestMessage);

      if (!response.IsSuccessStatusCode)
      {
        var defaultExchangeRate = System.Environment.GetEnvironmentVariable("DefaultExchangeRate");
        return double.Parse(defaultExchangeRate);
      }

      var result = await response.Content.ReadAsStringAsync();
      var jObject = JObject.Parse(result);
      return jObject["rates"]["SEK"].Value<double>();
    }

    static async Task<IEnumerable<PricePoint>> GetPricePoints(LocalDate localDate)
    {
      var securityToken = System.Environment.GetEnvironmentVariable("SecurityToken");
      var period = localDate.PlusDays(-1).ToString("yyyyMMdd2300", CultureInfo.InvariantCulture);
      var url = $"https://web-api.tp.entsoe.eu/api?documentType=A44&in_Domain=10Y1001A1001A46L&out_Domain=10Y1001A1001A46L&periodStart={period}&periodEnd={period}&securityToken={securityToken}";

      HttpResponseMessage response = await new HttpClient().GetAsync(url);

      if (!response.IsSuccessStatusCode) return new List<PricePoint>();

      var result = await response.Content.ReadAsStringAsync();

      XmlDocument doc = new XmlDocument();
      doc.LoadXml(result);

      var jObject = JObject.Parse(JsonConvert.SerializeXmlNode(doc));

      IList<JToken> results = jObject["Publication_MarketDocument"]["TimeSeries"]["Period"]["Point"].Children().ToList();

      return results.Select(result => result.ToObject<PricePoint>());
    }
  }
}
