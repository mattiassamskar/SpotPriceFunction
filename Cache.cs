using Newtonsoft.Json;
using NodaTime;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpotPrices
{
  public static class Cache
  {
    private static Stream _stream;
    public static PriceInfo PriceInfo { get; set; }

    public static void Hydrate(Stream stream)
    {
      _stream = stream;
      PriceInfo = ReadCache();
    }

    public static void StoreTodayPrices(IEnumerable<PricePoint> pricePoints)
    {
      PriceInfo.TodayPrices = pricePoints.ToList();
    }

    public static void StoreTomorrowPrices(IEnumerable<PricePoint> pricePoints)
    {
      PriceInfo.TomorrowPrices = pricePoints.ToList();
    }

    public static void StoreRate(double rate)
    {
      PriceInfo.Rate = rate;
    }

    public static void StoreToday(LocalDate localDate)
    {
      PriceInfo.Today = localDate;
    }

    public static void StoreTommorow(LocalDate localDate)
    {
      PriceInfo.Tomorrow = localDate;
    }

    public static void PersistCache()
    {
      TextWriter writer = null;
      try
      {
        writer = new StreamWriter(_stream);
        writer.Write(JsonConvert.SerializeObject(PriceInfo));
      }
      finally
      {
        if (writer != null)
          writer.Close();
      }
    }

    private static PriceInfo ReadCache()
    {
      TextReader reader = null;
      try
      {
        reader = new StreamReader(_stream);
        return JsonConvert.DeserializeObject<PriceInfo>(reader.ReadToEnd());
      }
      finally
      {
        if (reader != null)
          reader.Close();
      }
    }
  }
}
