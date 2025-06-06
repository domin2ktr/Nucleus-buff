﻿using System;
using Godot;

/// <summary>
///   Thriveopedia page displaying the patch map for the current game.
/// </summary>
/// <remarks>
///   <para>
///     Note a lot of this functionality is duplicated from PatchMapEditorComponent.
///   </para>
/// </remarks>
public partial class ThriveopediaPatchMapPage : ThriveopediaPage, IThriveopediaPage
{
#pragma warning disable CA2213
    [Export]
    private PatchMapDrawer mapDrawer = null!;

    [Export]
    private PatchDetailsPanel detailsPanel = null!;

    [Export]
    private Label seedLabel = null!;
#pragma warning restore CA2213

    private Patch playerPatchOnEntry = null!;

    public string PageName => "PatchMap";
    public string TranslatedPageName => Localization.Translate("THRIVEOPEDIA_PATCH_MAP_PAGE_TITLE");

    public Action<Patch>? OnSelectedPatchChanged { get; set; }

    public string ParentPageName => "CurrentWorld";

    public override void _Ready()
    {
        base._Ready();

        mapDrawer.OnSelectedPatchChanged = _ =>
        {
            UpdateShownPatchDetails();

            if (mapDrawer.SelectedPatch != null)
                OnSelectedPatchChanged?.Invoke(mapDrawer.SelectedPatch);
        };
    }

    public override void OnThriveopediaOpened()
    {
        if (CurrentGame == null)
            return;

        // TODO: update the player patch if they move patch in the editor
        SetGameForMap();
    }

    public override void UpdateCurrentWorldDetails()
    {
        if (CurrentGame == null)
            return;

        SetGameForMap();
        UpdateSeedLabel();
    }

    public override void OnNavigationPanelSizeChanged(bool collapsed)
    {
    }

    public override void OnTranslationsChanged()
    {
        UpdateShownPatchDetails();
        UpdateSeedLabel();
    }

    protected virtual void UpdateShownPatchDetails()
    {
        detailsPanel.SelectedPatch = mapDrawer.SelectedPatch;
        detailsPanel.UpdateShownPatchDetails();
    }

    private void UpdatePlayerPatch(Patch? patch)
    {
        mapDrawer.PlayerPatch = patch ?? playerPatchOnEntry;

        if (mapDrawer.Map == null)
            throw new InvalidOperationException("This can be only called after map is set from current game");

        if (mapDrawer.Map.UpdatePatchVisibility(mapDrawer.PlayerPatch))
            mapDrawer.MarkDirty();

        detailsPanel.CurrentPatch = mapDrawer.PlayerPatch;

        // Just in case this didn't get called already. Note that this may result in duplicate calls here
        UpdateShownPatchDetails();
    }

    private void SetGameForMap()
    {
        mapDrawer.Map = CurrentGame!.GameWorld.Map;
        playerPatchOnEntry = mapDrawer.Map?.CurrentPatch ??
            throw new InvalidOperationException("Map current patch needs to be set / SetMap needs to be called");

        // Make sure the map setting of fog of war always matches the world
        // These need to be called here in case this page is opened before the editor
        mapDrawer.Map.FogOfWar = CurrentGame.GameWorld.WorldSettings.FogOfWarMode;

        if (mapDrawer.Map.FogOfWar == FogOfWarMode.Ignored)
        {
            mapDrawer.Map.RevealAllPatches();
        }

        UpdatePlayerPatch(playerPatchOnEntry);
    }

    private void OnFindCurrentPatchPressed()
    {
        mapDrawer.CenterToCurrentPatch();
        mapDrawer.SelectedPatch = mapDrawer.PlayerPatch;
    }

    private void UpdateSeedLabel()
    {
        seedLabel.Text = Localization.Translate("SEED_LABEL")
            .FormatSafe(CurrentGame!.GameWorld.WorldSettings.Seed);
    }
}
