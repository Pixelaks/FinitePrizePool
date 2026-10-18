using System.Collections.Generic;
using System.Security.Cryptography;

namespace DealEngine
{
    /// <summary>Fisher-Yates shuffle using a cryptographic random number generator.</summary>
    public static class SecureShuffle
    {
        public static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1); // 0..i inclusive
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }
    }
}