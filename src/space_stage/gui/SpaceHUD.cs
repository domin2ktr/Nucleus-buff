﻿using Godot;

/// <summary>
///   HUD for the space stage. Very similar to <see cref="SocietyHUD"/>
/// </summary>
public partial class SpaceHUD : StrategyStageHUDBase<SpaceStage>, IStructureSelectionReceiver<SpaceStructureDefinition>
{
    // TODO: merge the common parts with the society stage hud into its own sub-scenes
#pragma warning disable CA2213
    [Export]
    private Label populationLabel = null!;

    [Export]
    private PlanetScreen planetScreenPopup = null!;

    [Export]
    private SpaceFleetInfoPopup fleetPopup = null!;

    [Export]
    private SpaceConstructionPopup constructionPopup = null!;

    [Export]
    private SpaceStructureInfoPopup structurePopup = null!;

    [Export]
    private Button descendButton = null!;

    private SpaceFleet? fleetToConstructWith;
#pragma warning restore CA2213

    private bool wasAscended;

    [Signal]
    public delegate void OnDescendPressedEventHandler();

    // TODO: real button referencing text for this
    protected override string UnPauseHelpText => "TODO: unpause text for this stage";

    public override void Init(SpaceStage containedInStage)
    {
        base.Init(containedInStage);

        UpdateButtonState();

        wasAscended = containedInStage.Ascended;

        // Setup multi level god tools signals, these are done this way as they would be pretty annoying to hook up
        // all over the place purely through Godot
        fleetPopup.Connect(StrategicUnitScreen<SpaceFleet>.SignalName.OnOpenGodTools, new Callable(containedInStage,
            nameof(StageBase.OpenGodToolsForEntity)));

        planetScreenPopup.Connect(PlanetScreen.SignalName.OnOpenGodTools, new Callable(containedInStage,
            nameof(StageBase.OpenGodToolsForEntity)));
    }

    public void OnAscended()
    {
        UpdateButtonState();

        if (!wasAscended)
        {
            wasAscended = true;

            // Close all windows to have them be reopened by the player to get the ascension stuff in them
            CloseAllOpenWindows();
        }
    }

    public void UpdatePopulationDisplay(long population)
    {
        populationLabel.Text = StringUtils.ThreeDigitFormat(population);
    }

    public void OpenPlanetScreen(PlacedPlanet planet)
    {
        planetScreenPopup.ShowForPlanet(planet, stage!.Ascended);
    }

    public void OpenFleetInfo(SpaceFleet fleet)
    {
        fleetPopup.ShowForUnit(fleet, stage!.Ascended);
    }

    public void CloseFleetInfo()
    {
        fleetPopup.Close();
    }

    public SpaceFleet? GetSelectedFleet()
    {
        return fleetPopup.OpenedForUnit;
    }

    public void OpenStructureInfo(PlacedSpaceStructure structure)
    {
        structurePopup.ShowForStructure(structure);
    }

    /// <summary>
    ///   Closes all open windows, called when something really important is being shown on screen
    /// </summary>
    public void CloseAllOpenWindows()
    {
        planetScreenPopup.Close();
        fleetPopup.Close();
        constructionPopup.Close();
        structurePopup.Close();
        researchScreen.Close();
    }

    public void ShowConstructionOptionsForFleet(SpaceFleet fleet)
    {
        fleetToConstructWith = fleet;

        // TODO: maybe this will need to fleet if some structures would have special requirements for building them
        constructionPopup.OpenWithStructures(stage!.CurrentGame!.TechWeb.GetAvailableSpaceStructures(), this,
            stage.SocietyResources);
    }

    public void OnStructureTypeSelected(SpaceStructureDefinition structureDefinition)
    {
        if (fleetToConstructWith == null)
        {
            GD.PrintErr("No fleet to construct with set");
            return;
        }

        stage!.StartPlacingStructure(fleetToConstructWith, structureDefinition);
    }

    private void UpdateButtonState()
    {
        descendButton.Visible = stage?.CurrentGame?.Ascended == true;
    }

    private void ForwardDescendPress()
    {
        GUICommon.Instance.PlayButtonPressSound();

        EmitSignal(SignalName.OnDescendPressed);
    }
}
