namespace Helper.Lib;

using System.Text;

public class StringHelper
{
    /// <summary>
    /// Converts a string into a CSV cell output.
    /// </summary>
    /// <param name="str">The string to convert.</param>
    /// <returns>The converted string in CSV cell format.</returns>
    public static string StringToCSVCell(string str)
    {
        bool mustQuote = str.Contains(',') || str.Contains('"') || str.Contains('\r') || str.Contains('\n');
        if (mustQuote)
        {
            StringBuilder sb = new ();
            sb.Append('"');
            foreach (char nextChar in str)
            {
                sb.Append(nextChar);
                if (nextChar == '"')
                {
                    sb.Append('"');
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return str;
    }
}