using Atomcraft;

namespace PixelInspector;

/// <summary>
/// Writes a temperature the way the player has asked to read temperatures.
///
/// <para>The game has a Celsius, Kelvin and Fahrenheit setting, and <c>Temperature.ToString</c>
/// already honours it. This does not use it, for one reason: that method writes a degree sign,
/// and the bitmap font covers printable ASCII plus two characters this mod needed. A degree sign
/// would come out as the font's fallback, which is a question mark. So the conversion is the
/// game's own arithmetic, borrowed from <c>Temperature</c>.</para>
///
/// <para><b>Digits only, with no unit at all.</b> A unit letter was tried and dropped. The reading
/// is four characters at most and shares a cell with everything else drawn on it, so a fifth spent
/// on a letter is the one that pushes a name into being cut. It says less than it looks like it
/// does, too: it names the unit the reading is in, which the player chose, and which is therefore
/// the one thing about the number they already know. The ambiguity it guards against -- 273 K
/// against 273 C -- needs the player to be reading in a unit they did not pick.</para>
/// </summary>
internal static class Temperatures
{
    /// <summary>
    /// A kelvin figure as the player would read it: digits, in the unit their settings name.
    /// </summary>
    internal static string Format(int kelvin)
    {
        var temperature = new Temperature(kelvin);
        return Game.DeviceSettings?.TemperatureUnits switch
        {
            TemperatureUnits.Celsius    => $"{temperature.Celsius}",
            TemperatureUnits.Fahrenheit => $"{temperature.Fahrenheit}",
            // Kelvin, and the fallback for a settings object that does not exist yet: the
            // material's own name is in kelvin, so it is the answer that is never wrong.
            _                           => $"{kelvin}",
        };
    }
}
