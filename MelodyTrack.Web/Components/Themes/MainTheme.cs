using MelodyTrack.Web.Components.Palettes;
using MudBlazor;

namespace MelodyTrack.Web.Components.Themes;

public static class MainTheme
{
    public static MudTheme Theme => new()
    {
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["EB Garamond", "Georgia", "serif"],
                FontSize = "1rem",
                LetterSpacing = "normal"
            },
            H1 = new H1Typography { FontSize = "3.5rem" },
            H2 = new H2Typography { FontSize = "2.8rem" },
            H3 = new H3Typography { FontSize = "2.2rem" },
            H4 = new H4Typography { FontSize = "1.8rem" },
            H5 = new H5Typography { FontSize = "1.5rem" },
            Subtitle1 = new Subtitle1Typography { FontSize = "1.25rem" },
            Body1 = new Body1Typography { FontSize = "1.1rem", LineHeight = "1.6" },
            Body2 = new Body2Typography { FontSize = "1rem" },
            Caption = new CaptionTypography { FontSize = "0.9rem" }
        },
        PaletteLight = new VintageParchmentPalette(),
        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "280px",
            DrawerWidthRight = "280px",
            AppbarHeight = "80px"
        },
        ZIndex = new ZIndex { Drawer = 1300, Dialog = 1400 }
    };
}