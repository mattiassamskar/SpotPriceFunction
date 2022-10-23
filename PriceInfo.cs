using NodaTime;
using System.Collections.Generic;

namespace SpotPrices
{
  public class PriceInfo
  {
    public double Rate { get; set; }
    public LocalDate Today { get; set; }
    public LocalDate Tomorrow { get; set; }
    public List<PricePoint> TodayPrices { get; set; }
    public List<PricePoint> TomorrowPrices { get; set; }
  }
}
