using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// A small recursive-descent JSON reader for the map baker (GeoJSON is nested
/// arrays, which JsonUtility can't read). Objects become
/// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double,
/// plus string / bool / null. Editor-only; it reads the source file once.
/// </summary>
public static class MiniJson
{
    public static object Parse(string json)
    {
        int i = 0;
        object v = Value(json, ref i);
        Skip(json, ref i);
        if (i != json.Length) throw new System.FormatException($"JSON: trailing text at {i}");
        return v;
    }

    static void Skip(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
    }

    static object Value(string s, ref int i)
    {
        Skip(s, ref i);
        if (i >= s.Length) throw new System.FormatException("JSON: unexpected end");
        char c = s[i];
        if (c == '{') return Obj(s, ref i);
        if (c == '[') return Arr(s, ref i);
        if (c == '"') return Str(s, ref i);
        if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        return Num(s, ref i);
    }

    static Dictionary<string, object> Obj(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++; // {
        Skip(s, ref i);
        if (s[i] == '}') { i++; return d; }
        while (true)
        {
            Skip(s, ref i);
            string k = Str(s, ref i);
            Skip(s, ref i);
            if (s[i] != ':') throw new System.FormatException($"JSON: ':' expected at {i}");
            i++;
            d[k] = Value(s, ref i);
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; return d; }
            throw new System.FormatException($"JSON: ',' or '}}' expected at {i}");
        }
    }

    static List<object> Arr(string s, ref int i)
    {
        var l = new List<object>();
        i++; // [
        Skip(s, ref i);
        if (s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(Value(s, ref i));
            Skip(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; return l; }
            throw new System.FormatException($"JSON: ',' or ']' expected at {i}");
        }
    }

    static string Str(string s, ref int i)
    {
        if (s[i] != '"') throw new System.FormatException($"JSON: string expected at {i}");
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                default: sb.Append(e); break;
            }
        }
        throw new System.FormatException("JSON: unterminated string");
    }

    static double Num(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (i == start) throw new System.FormatException($"JSON: unexpected '{s[i]}' at {i}");
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
