using System.Globalization;
using System.Text;

namespace DealEngine
{
    public static class DealSizeTable
    {
        public static string BuildCsv(DealConfig config)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Mode,BetDollars,PairedCycles,PaidSpins,PrizeTickets,LossTickets,TotalWagers,TotalPayouts,Status");

            for (int m = 0; m < config.modes.Count; m++)
            {
                foreach (long bet in config.betLevelsCents)
                {
                    DealSize s = DealSizeCalculator.Calculate(config, m, bet);
                    sb.Append(s.ModeName).Append(',')
                      .Append(Dollars(s.BetCents)).Append(',')
                      .Append(s.PairedCycles).Append(',')
                      .Append(s.IsValid ? s.PaidSpins.ToString() : "").Append(',')
                      .Append(s.PrizeTickets).Append(',')
                      .Append(s.IsValid ? s.LossTickets.ToString() : "").Append(',')
                      .Append(s.IsValid ? Dollars(s.TotalWagersCents) : "").Append(',')
                      .Append(Dollars(s.TotalPayoutsCents)).Append(',')
                      .AppendLine(s.IsValid ? "OK" : "REJECTED: " + s.RejectReason.Replace(",", ";"));
                }
            }
            return sb.ToString();
        }

        private static string Dollars(long cents)
        {
            return (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}