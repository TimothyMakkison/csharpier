using System.Globalization;

namespace SyntaxFinder;

public static class ResultWriter
{
    public static void WriteMatching(int total, int matching)
    {
        if (total == 0)
        {
            return;
        }

        if (matching > total)
        {
            Console.WriteLine("Matching was > than Total, so you did something wrong.");
        }

        WriteResult("Matching", matching.ToString("n0", CultureInfo.InvariantCulture));
        WriteResult("Total", total.ToString("n0", CultureInfo.InvariantCulture));
        WriteResult(
            "Percent",
            (Convert.ToDecimal(matching) / total * 100).ToString("n", CultureInfo.InvariantCulture)
                + "%"
        );
    }

    public static void WriteResult(string label, string value)
    {
        Console.WriteLine((label + ":").PadRight(20) + value.PadLeft(10));
    }
}
