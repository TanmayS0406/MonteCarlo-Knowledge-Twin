namespace MonteCarlo.Domain.Common;

/// <summary>Working-day arithmetic (Mon-Fri). Day 0 is the first working day on/after the start date.</summary>
public static class WorkingCalendar
{
    public static bool IsWorkingDay(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateOnly FirstWorkingDay(DateOnly d)
    {
        while (!IsWorkingDay(d)) d = d.AddDays(1);
        return d;
    }

    public static DateOnly AddWorkingDays(DateOnly start, int days)
    {
        var d = FirstWorkingDay(start);
        var step = days >= 0 ? 1 : -1;
        var left = Math.Abs(days);
        while (left > 0)
        {
            d = d.AddDays(step);
            if (IsWorkingDay(d)) left--;
        }
        return d;
    }

    /// <summary>Number of working days from a to b (b exclusive). Negative if b is before a.</summary>
    public static int WorkingDaysBetween(DateOnly a, DateOnly b)
    {
        if (b < a) return -WorkingDaysBetween(b, a);
        var count = 0;
        for (var d = a; d < b; d = d.AddDays(1))
            if (IsWorkingDay(d)) count++;
        return count;
    }
}
