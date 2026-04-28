namespace ITSS
{
    public static class MathExtensions
    {
        public static decimal ZDiv(this decimal? num, decimal? den)
        {
            if (num is null || den is null || den == 0)
                return 0m;
            return (decimal)num / (decimal)den;
        }

        // Strips all non-numeric characters except decimal point
        public static string MakeNumeric(this string s)
            => new string(s.Where(c => char.IsDigit(c) || c == '.').ToArray());

        // Returns true only if every character is a digit (no decimals or signs)
        public static bool IsNumeric(this string text)
            => !string.IsNullOrEmpty(text) && text.All(char.IsDigit);

        // Returns true if the string represents a valid decimal number
        public static bool IsDecimal(this string text)
            => decimal.TryParse(text, out _);

        public static decimal RoundTo(this decimal value, int decimals)
            => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        public static double RoundTo(this double value, int decimals)
            => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        public static decimal Clamp(this decimal value, decimal min, decimal max)
            => value < min ? min : value > max ? max : value;

        public static int Clamp(this int value, int min, int max)
            => value < min ? min : value > max ? max : value;
    }
}
