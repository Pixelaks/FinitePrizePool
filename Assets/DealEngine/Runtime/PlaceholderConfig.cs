using System.Collections.Generic;

namespace DealEngine
{
    /// <summary>
    /// TEMPORARY config with INVENTED bag values, used until the real
    /// "Finite Pool Mathematics" bags are available. Replace with the real numbers.
    /// </summary>
    public static class PlaceholderConfig
    {
        public static DealConfig Create()
        {
            DealConfig config = new DealConfig
            {
                configVersion = "placeholder-1",
                targetRtpBasisPoints = 9000,
                bonusUnitCents = 100,
                maxGenerationAttempts = 1000
            };

            // 20 bet levels: $0.25 to $5.00 in $0.25 steps
            for (int i = 1; i <= 20; i++)
                config.betLevelsCents.Add(25L * i);

            config.modes.Add(MakeMode("Inner", 2, 3, 5, 10));
            config.modes.Add(MakeMode("Outer", 3, 5, 8, 20));
            return config;
        }

        private static ModeConfig MakeMode(string name, int regA, int regB, int bigA, int bigB)
        {
            ModeConfig mode = new ModeConfig { name = name };
            mode.regularBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Multiplier, value = regA, count = 3 });
            mode.regularBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Multiplier, value = regB, count = 1 });
            mode.bigBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Multiplier, value = bigA, count = 1 });
            mode.bigBag.prizes.Add(new PrizeEntry { kind = PrizeKind.Bonus, value = bigB, count = 1 });
            return mode;
        }
    }
}