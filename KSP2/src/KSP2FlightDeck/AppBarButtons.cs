using System;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace FlightDeck.Ksp2
{
    /// <summary>
    /// KSP 2 reaches mod UI through the app bar drawer, so the Flight Deck status panel gets a
    /// button there (in flight and in the KSC), mirroring the settings toggle.
    /// </summary>
    internal static class AppBarButtons
    {
        private const string InFlightId = "KSPFlightDeck2.InFlight";
        private const string KscId = "KSPFlightDeck2.Ksc";

        public static void Register()
        {
            try
            {
                Sprite icon = CreateIcon();

                Appbar.RegisterAppButton("Flight Deck", InFlightId, icon, pressed =>
                {
                    FlightDeckAddon addon = FlightDeckAddon.Instance;
                    if (addon != null)
                    {
                        addon.SetPanelVisible(pressed);
                    }
                });

                Appbar.RegisterKSCAppButton("Flight Deck", KscId, icon, () =>
                {
                    FlightDeckAddon addon = FlightDeckAddon.Instance;
                    if (addon != null)
                    {
                        addon.SetPanelVisible(!addon.PanelVisible);
                    }
                });

                Ksp2Log.Info("Registered app bar buttons (flight and KSC).");
            }
            catch (Exception ex)
            {
                Ksp2Log.Error("Could not register the app bar buttons.", ex);
            }
        }

        /// <summary>Stream Deck style 2x2 key grid.</summary>
        private static Sprite CreateIcon()
        {
            const int size = 32;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var white = new Color(1f, 1f, 1f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            FillRoundedRect(texture, 4, 4, 11, 11, 3, white);
            FillRoundedRect(texture, 17, 4, 11, 11, 3, white);
            FillRoundedRect(texture, 4, 17, 11, 11, 3, white);
            FillRoundedRect(texture, 17, 17, 11, 11, 3, white);

            texture.Apply();
            return Appbar.GetAppBarIconFromTexture(texture, size, size);
        }

        private static void FillRoundedRect(Texture2D texture, int x0, int y0, int width, int height, int radius, Color color)
        {
            for (int y = y0; y < y0 + height; y++)
            {
                for (int x = x0; x < x0 + width; x++)
                {
                    int dx = 0;
                    int dy = 0;

                    if (x < x0 + radius) dx = x0 + radius - x;
                    else if (x >= x0 + width - radius) dx = x - (x0 + width - radius - 1);

                    if (y < y0 + radius) dy = y0 + radius - y;
                    else if (y >= y0 + height - radius) dy = y - (y0 + height - radius - 1);

                    if ((dx * dx) + (dy * dy) <= radius * radius)
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }
    }
}
