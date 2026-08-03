using Microsoft.Xna.Framework.Input;

namespace Client.Main.Helpers
{
    /// <summary>
    /// Allocation-free key scan used by polling-based desktop text controls.
    /// The order follows the numeric Keys ordering returned by KeyboardState.GetPressedKeys().
    /// </summary>
    internal static class DesktopTextInputKeys
    {
        internal static readonly Keys[] DigitsAndCommands =
        {
            Keys.Back,
            Keys.Enter,
            Keys.Escape,
            Keys.D0,
            Keys.D1,
            Keys.D2,
            Keys.D3,
            Keys.D4,
            Keys.D5,
            Keys.D6,
            Keys.D7,
            Keys.D8,
            Keys.D9,
            Keys.NumPad0,
            Keys.NumPad1,
            Keys.NumPad2,
            Keys.NumPad3,
            Keys.NumPad4,
            Keys.NumPad5,
            Keys.NumPad6,
            Keys.NumPad7,
            Keys.NumPad8,
            Keys.NumPad9
        };

        internal static readonly Keys[] All =
        {
            Keys.Back,
            Keys.Enter,
            Keys.Escape,
            Keys.Space,
            Keys.Delete,
            Keys.D0,
            Keys.D1,
            Keys.D2,
            Keys.D3,
            Keys.D4,
            Keys.D5,
            Keys.D6,
            Keys.D7,
            Keys.D8,
            Keys.D9,
            Keys.A,
            Keys.B,
            Keys.C,
            Keys.D,
            Keys.E,
            Keys.F,
            Keys.G,
            Keys.H,
            Keys.I,
            Keys.J,
            Keys.K,
            Keys.L,
            Keys.M,
            Keys.N,
            Keys.O,
            Keys.P,
            Keys.Q,
            Keys.R,
            Keys.S,
            Keys.T,
            Keys.U,
            Keys.V,
            Keys.W,
            Keys.X,
            Keys.Y,
            Keys.Z,
            Keys.NumPad0,
            Keys.NumPad1,
            Keys.NumPad2,
            Keys.NumPad3,
            Keys.NumPad4,
            Keys.NumPad5,
            Keys.NumPad6,
            Keys.NumPad7,
            Keys.NumPad8,
            Keys.NumPad9,
            Keys.OemSemicolon,
            Keys.OemPlus,
            Keys.OemComma,
            Keys.OemMinus,
            Keys.OemPeriod,
            Keys.OemQuestion,
            Keys.OemTilde,
            Keys.OemOpenBrackets,
            Keys.OemPipe,
            Keys.OemCloseBrackets,
            Keys.OemQuotes
        };
    }
}
