using Newtonsoft.Json;

public class PricePoint
{
  [JsonProperty("price.amount")]
  public double Amount { get; set; }
}