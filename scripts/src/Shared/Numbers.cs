string V3ToString(Vector3D v)
{
    return v.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + v.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + v.Z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

string DirectionToString(Vector3D v)
{
    return v.X.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + v.Y.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)
        + ":" + v.Z.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
}

string Number2(double value)
{
    return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

string Number3(double value)
{
    return value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}

string NumberR(double value)
{
    return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

bool TryParseNumber(string text, out double value)
{
    return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
}

bool TryParseV3(string s, out Vector3D v)
{
    v = Vector3D.Zero;
    if (string.IsNullOrWhiteSpace(s))
        return false;
    string[] p = s.Split(':');
    if (p.Length != 3)
        return false;
    double x, y, z;
    if (!double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) || !double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
        return false;
    v = new Vector3D(x, y, z);
    return true;
}
