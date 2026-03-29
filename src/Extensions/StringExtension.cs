public static class StringExtension
{
    public static string KeyFormat(this string str, params (string, object)[] formatPair)
    {
        if(formatPair.Length < 1) return str;

        foreach((string, object) pair in formatPair)
        {
            string token = "{" + pair.Item1 + "}";
            if(str.Contains(token))
                str = str.Replace(token, pair.Item2.ToString());
        }

        return str;
    }
}
