namespace MelodyTrack.Web.Components.Palettes;

using MudBlazor;
using MudBlazor.Utilities;

public sealed class VintageParchmentPalette : PaletteLight
{
    public VintageParchmentPalette()
    {
        // Core palette – directly from your MUI theme
        Primary = "#7D5A4B";                    // primary.main
        PrimaryContrastText = "#F7E7C6";        // primary.contrastText

        Secondary = "#B18E6F";                  // secondary.main
        SecondaryContrastText = "#3E2F29";      // secondary.contrastText

        Tertiary = "#6D8F6C";                   // success.main (used as tertiary for earthy green)
        TertiaryContrastText = "#F7E7C6";

        Info = "#6C8C9C";                       // info.main
        InfoContrastText = "#F7E7C6";

        Success = "#6D8F6C";                    // success.main
        SuccessContrastText = "#F7E7C6";

        Warning = "#B58D5D";                    // warning.main
        WarningContrastText = "#3E2F29";

        Error = "#9C5858";                      // error.main
        ErrorContrastText = "#F7E7C6";

        // Text colors
        TextPrimary = "#3E2F29";
        TextSecondary = "#6F5C4F";
        TextDisabled = "#A59B8C";

        // Backgrounds
        Background = "#F7E7C6";                 // background.default
        BackgroundGray = "#EDE0C8";
        Surface = "#EDE0C8";                    // background.paper equivalent
        DrawerBackground = "#EDE0C8";
        AppbarBackground = "#F7E7C6";           // Matches page background
        AppbarText = "#3E2F29";

        // Borders & Lines
        LinesDefault = "#D3B89E";               // secondary.light – soft border
        LinesInputs = "#8C6F5A";                // secondary.dark – input borders
        Divider = "#8C6F5A";
        DividerLight = new MudColor("#8C6F5A").SetAlpha(0.5);

        // Table styling
        TableLines = "#D3B89E";
        TableStriped = new MudColor("#000000").SetAlpha(0.02);
        TableHover = new MudColor("#000000").SetAlpha(0.06);

        // Action states
        ActionDefault = new MudColor("#3E2F29").SetAlpha(0.7);
        ActionDisabled = new MudColor("#3E2F29").SetAlpha(0.38);
        ActionDisabledBackground = new MudColor("#3E2F29").SetAlpha(0.12);

        // Overlays
        OverlayDark = new MudColor("#3E2F29").SetAlpha(0.5).ToString(MudColorOutputFormats.RGBA);
        OverlayLight = new MudColor("#F7E7C6").SetAlpha(0.8).ToString(MudColorOutputFormats.RGBA);

        // Optional: fine-tune hover/ripple for vintage feel
        HoverOpacity = 0.1;
        RippleOpacity = 0.15;
        RippleOpacitySecondary = 0.25;
    }
}