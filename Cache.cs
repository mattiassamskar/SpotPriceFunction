using Azure.Storage.Blobs;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using NodaTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpotPrices
{
  public static class Cache
  {
    private static BlobClient _blobClient;
    public static PriceInfo PriceInfo { get; set; }

    public static async Task Hydrate()
    {
      try
      {
        var blobContainerClient = new BlobContainerClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), "cache");
        _blobClient = blobContainerClient.GetBlobClient("spotpricecache.json");
        var priceInfo = await ReadCache();
        PriceInfo = priceInfo != null ? priceInfo : new PriceInfo();
      }
      finally
      {
        if (PriceInfo == null) PriceInfo = new PriceInfo();
      }
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

    public static async Task PersistCache()
    {
      var stream = await _blobClient.OpenWriteAsync(true);
      stream.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(PriceInfo)));
      stream.Close();
    }

    private async static Task<PriceInfo> ReadCache()
    {
      try
      {
        if (!_blobClient.Exists()) return new PriceInfo();
        var stream = await _blobClient.OpenReadAsync();
        var reader = new StreamReader(stream);
        return JsonConvert.DeserializeObject<PriceInfo>(reader.ReadToEnd());
      }
      finally
      {
      }
    }
  }
}
