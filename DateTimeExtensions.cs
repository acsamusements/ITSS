namespace ITSS
{
    public static class DateTimeExtensions
    {
        public static bool IsWeekend(this DateTime date)
            => date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

        public static bool IsWeekday(this DateTime date)
            => !date.IsWeekend();

        public static DateTime StartOfDay(this DateTime date)
            => date.Date;

        public static DateTime EndOfDay(this DateTime date)
            => date.Date.AddDays(1).AddTicks(-1);

        public static DateTime StartOfMonth(this DateTime date)
            => new(date.Year, date.Month, 1);

        public static DateTime EndOfMonth(this DateTime date)
            => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

        public static DateTime StartOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Sunday)
        {
            int diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
            return date.AddDays(-diff).Date;
        }

        // Calculates age in whole years from a birth date
        public static int ToAge(this DateTime birthDate)
        {
            DateTime today = DateTime.Today;
            int age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }

        public static bool IsBetween(this DateTime date, DateTime start, DateTime end)
            => date >= start && date <= end;

        // Returns a friendly relative description ("Today", "Yesterday", "3 days ago", etc.)
        public static string ToRelativeString(this DateTime date)
        {
            TimeSpan diff = DateTime.Now - date;

            return diff.TotalSeconds switch
            {
                < 60 => "Just now",
                < 3600 => $"{(int)diff.TotalMinutes} minute{((int)diff.TotalMinutes == 1 ? "" : "s")} ago",
                < 86400 => $"{(int)diff.TotalHours} hour{((int)diff.TotalHours == 1 ? "" : "s")} ago",
                < 172800 => "Yesterday",
                < 604800 => $"{(int)diff.TotalDays} days ago",
                _ => date.ToShortDateString()
            };
        }
    }
}
