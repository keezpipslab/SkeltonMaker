using UnityEngine;

namespace SkeletonMaker
{
    /// <summary>
    /// The button behind the color baths that changes what colors they hold:
    /// each press goes on to the next palette, and after the last back to the
    /// colors the baths have in the scene. A palette is five colors; the sixth
    /// bath gets white, or black when the palette has a white of its own.
    /// Only the baths change - what has been painted keeps its color. Its
    /// baths are the ones under its parent (the "Color Baths" object), so it
    /// comes and goes with them. See PushButton for how it's pressed.
    /// </summary>
    public class PaletteButton : PushButton
    {
        // From this light on (every channel) a color counts as the palette's white.
        private const float WhiteFrom = 0.92f;

        private static readonly (string name, Color[] colors)[] Palettes =
        {
            ("Dusk harbour", new[] { Hex(0x1B2A41), Hex(0x324A5F), Hex(0x7A9E9F), Hex(0xE0D8C3), Hex(0xF2A65A) }),
            ("Terracotta garden", new[] { Hex(0x4A2C2A), Hex(0xB5523B), Hex(0xE8A87C), Hex(0xF3E9DC), Hex(0x6B8F71) }),
            ("Nordic morning", new[] { Hex(0xF7F9F9), Hex(0xD5E1E6), Hex(0x8FA8B8), Hex(0x4B6478), Hex(0x1F2D3A) }),
            ("Citrus pop", new[] { Hex(0xFF7A1A), Hex(0xFFD23F), Hex(0xF6F4EC), Hex(0x1FA8A0), Hex(0x22313F) }),
            ("Moss and stone", new[] { Hex(0x22301F), Hex(0x4F6B3A), Hex(0x9DB07E), Hex(0xDAD7C5), Hex(0x7A7468) }),
            ("Berry night", new[] { Hex(0x1A1026), Hex(0x4B1D52), Hex(0xA23B72), Hex(0xF18FB0), Hex(0xFBE8EE) }),
            ("Desert neon", new[] { Hex(0x2B193D), Hex(0xE2583A), Hex(0xF2B544), Hex(0x76C7B7), Hex(0xF7F3E3) }),
            ("Ink and paper", new[] { Hex(0x0F0F10), Hex(0x3A3A3C), Hex(0x8C8C8A), Hex(0xD9D4CA), Hex(0xC8452E) }),
        };

        private ColorBath[] baths;
        private Color[] own;   // the baths' colors before the first press
        private int current = -1; // -1: their own colors

        protected override string Caption => "Colors\n" + (current < 0 ? "own" : Palettes[current].name);

        protected override void Press()
        {
            if (baths == null)
            {
                baths = transform.parent != null
                    ? transform.parent.GetComponentsInChildren<ColorBath>(true)
                    : FindObjectsByType<ColorBath>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                own = new Color[baths.Length];
                for (int i = 0; i < baths.Length; i++) own[i] = baths[i].Color;
            }

            current = current + 1 < Palettes.Length ? current + 1 : -1;

            for (int i = 0; i < baths.Length; i++)
            {
                if (baths[i] == null) continue;
                baths[i].Color = current < 0 ? own[i] : ColorOf(Palettes[current].colors, i);
            }
        }

        // The palette's colors, then the extra one, and round again for more baths than that.
        private static Color ColorOf(Color[] palette, int index)
        {
            index %= palette.Length + 1;
            if (index < palette.Length) return palette[index];

            foreach (var color in palette)
                if (Mathf.Min(color.r, color.g, color.b) >= WhiteFrom) return Color.black;
            return Color.white;
        }

        private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
