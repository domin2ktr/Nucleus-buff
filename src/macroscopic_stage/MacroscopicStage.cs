﻿using System;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Main class for managing the macroscopic stage
/// </summary>
[JsonObject(IsReference = true)]
[SceneLoadedClass("res://src/macroscopic_stage/MacroscopicStage.tscn")]
[DeserializedCallbackTarget]
[UseThriveSerializer]
public partial class MacroscopicStage : CreatureStageBase<MacroscopicCreature, DummyWorldSimulation>
{
    private const string STAGE_TRANSITION_MOUSE_LOCK = "toSocietyStage";

    [JsonProperty]
    [AssignOnlyChildItemsOnDeserialize]
    private ISpawnSystem dummySpawner = null!;

#pragma warning disable CA2213
    [Export]
    private InteractableSystem interactableSystem = null!;

    [Export]
    private InteractablePopup interactionPopup = null!;

    [Export]
    private ProgressBarSystem progressBarSystem = null!;

    [Export]
    private SelectBuildingPopup selectBuildingPopup = null!;

    [Export]
    private WorldEnvironment worldEnvironmentNode = null!;

    [Export]
    private DirectionalLight3D worldLightNode = null!;

    private Camera3D? animationCamera;
#pragma warning restore CA2213

    /// <summary>
    ///   Used to detect when the player automatically advances stages in the editor (awakening is explicit with a
    ///   button as it should be only used after moving to land)
    /// </summary>
    [JsonProperty]
    private MacroscopicSpeciesType previousPlayerStage;

    [JsonProperty]
    private double moveToSocietyTimer;

    [JsonProperty]
    private Transform3D societyCameraAnimationStart = Transform3D.Identity;

    [JsonProperty]
    private Transform3D societyCameraAnimationEnd = Transform3D.Identity;

    [JsonProperty]
    private Vector3 animationEndCameraLookPoint;

    [JsonProperty]
    private Transform3D firstSocietyCenterTransform;

    [JsonProperty]
    private bool movingToSocietyStage;

    [JsonProperty]
    [AssignOnlyChildItemsOnDeserialize]
    public MacroscopicCamera PlayerCamera { get; private set; } = null!;

    [JsonProperty]
    [AssignOnlyChildItemsOnDeserialize]
    public MacroscopicHUD HUD { get; private set; } = null!;

    // TODO: create a multicellular equivalent class
    [JsonIgnore]
    public PlayerInspectInfo HoverInfo { get; private set; } = null!;

    [JsonIgnore]
    public override bool HasPlayer => Player != null;

    public override MainGameState GameState => MainGameState.MacroscopicStage;

    // TODO: change when there is dying implemented
    [JsonIgnore]
    public override bool HasAlivePlayer => HasPlayer;

    [JsonIgnore]
    protected override ICreatureStageHUD BaseHUD => HUD;

    private LocalizedString CurrentPatchName =>
        GameWorld.Map.CurrentPatch?.Name ?? throw new InvalidOperationException("no current patch");

    public override void _Ready()
    {
        base._Ready();

        ResolveNodeReferences();

        HUD.Init(this);

        // HoverInfo.Init(Camera, Clouds);

        interactableSystem.Init(PlayerCamera.CameraNode, rootOfDynamicallySpawned);
        interactionPopup.OnInteractionSelectedHandler += ForwardInteractionSelectionToPlayer;

        progressBarSystem.Init(PlayerCamera.CameraNode, rootOfDynamicallySpawned);

        SetupStage();
    }

    public override void ResolveNodeReferences()
    {
        if (NodeReferencesResolved)
            return;

        base.ResolveNodeReferences();

        HUD = GetNode<MacroscopicHUD>("MacroscopicHUD");
        HoverInfo = GetNode<PlayerInspectInfo>("PlayerLookingAtInfo");

        // TODO: implement macroscopic specific look at info, for now it's disabled by removing it
        HoverInfo.Free();

        PlayerCamera = world.GetNode<MacroscopicCamera>("PlayerCamera");

        // These need to be created here as well for child property save load to work
        // TODO: systems

        // We don't actually spawn anything currently, and anyway will want a different spawn system for late
        // multicellular
        dummySpawner = new DummySpawnSystem();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (gameOver || StageLoadingState != LoadState.NotLoading)
            return;

        if (playerExtinctInCurrentPatch)
            return;

        if (Player != null)
        {
            var playerPosition = Player.GlobalPosition;

            if (Player.Species.MacroscopicType == MacroscopicSpeciesType.Awakened)
            {
                // TODO: player interaction reach modifier from the species
                interactableSystem.UpdatePlayerPosition(playerPosition, 0);
                interactableSystem.SetActive(true);
            }
            else
            {
                interactableSystem.SetActive(false);
            }

            progressBarSystem.UpdatePlayerPosition(playerPosition);

            // HUD.UpdateRadiationBar(compounds.GetCompoundAmount(Compound.Radiation),
            //     compounds.GetCapacityForCompound(Compound.Radiation), Constants.RADIATION_WARNING);
        }

        if (movingToSocietyStage)
        {
            if (animationCamera == null)
                throw new InvalidOperationException("Animation camera not set");

            moveToSocietyTimer += delta;
            var interpolationProgress = moveToSocietyTimer / Constants.SOCIETY_STAGE_ENTER_ANIMATION_DURATION;

            if (interpolationProgress >= 1)
            {
                interpolationProgress = 1;

                // Fade to black and queue the transition
                HUD.EnsureGameIsUnpausedForEditor();

                GD.Print("Starting fade out to society stage");

                // The fade is pretty long here to give some time after the camera stops moving before the fade out
                // is complete
                TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeOut, 3.5f, SwitchToSocietyScene, false);
                MovingToEditor = true;
                movingToSocietyStage = false;
            }

            // TODO: switch to some other animation type like quintic once that is usable without a tween node
            animationCamera.GlobalTransform =
                societyCameraAnimationStart.InterpolateWith(societyCameraAnimationEnd, (float)interpolationProgress);
        }

        // TODO: notify metrics
    }

    public override void StartMusic()
    {
        // Change music based on how far along in the game the player is
        if (GameWorld.PlayerSpecies is MacroscopicSpecies macroscopicSpecies)
        {
            if (macroscopicSpecies.MacroscopicType == MacroscopicSpeciesType.Awakened)
            {
                Jukebox.Instance.PlayCategory("AwakeningStage");
                return;
            }

            if (macroscopicSpecies.MacroscopicType == MacroscopicSpeciesType.Aware)
            {
                Jukebox.Instance.PlayCategory("AwareStage");
                return;
            }
        }

        Jukebox.Instance.PlayCategory("MacroscopicStage");
    }

    public override void OnFinishLoading(Save save)
    {
        OnFinishLoading();
    }

    public override void StartNewGame()
    {
        CurrentGame = GameProperties.StartNewMacroscopicGame(new WorldGenerationSettings());

        UpdatePatchSettings();

        base.StartNewGame();
    }

    // TODO: different pause key as space will be for jumping
    // [RunOnKeyDown("g_pause")]
    public void PauseKeyPressed()
    {
        // Check nothing else has keyboard focus and pause the game
        if (HUD.GetFocusOwner() == null)
        {
            HUD.PauseButtonPressed(!HUD.Paused);
        }
    }

    public override void MoveToEditor()
    {
        if (Player?.Dead != false)
        {
            GD.PrintErr("Player object disappeared or died while transitioning to the editor");
            return;
        }

        if (CurrentGame == null)
            throw new InvalidOperationException("Stage has no current game");

        var scene = SceneManager.Instance.LoadScene(MainGameState.MacroscopicEditor);

        Node sceneInstance = scene.Instantiate();
        var editor = (MacroscopicEditor)sceneInstance;

        editor.CurrentGame = CurrentGame;
        editor.ReturnToStage = this;

        GiveReproductionPopulationBonus();

        // We don't free this here as the editor will return to this scene
        if (SceneManager.Instance.SwitchToScene(sceneInstance, true) != this)
        {
            throw new Exception("failed to keep the current scene root");
        }

        MovingToEditor = false;
    }

    public override void OnReturnFromEditor()
    {
        UpdatePatchSettings();

        base.OnReturnFromEditor();

        ProceduralDataCache.Instance.OnEnterState(MainGameState.MacroscopicStage);

        // TODO:
        // // Spawn free food if difficulty settings call for it
        // if (GameWorld.WorldSettings.FreeGlucoseCloud)
        // {
        // }

        // Spawn another cell from the player species
        // This is done first to ensure that the player colony is still intact for spawn separation calculation
        var parent = Player!.SpawnOffspring();
        parent.BecomeFullyGrown();

        // Update the player's creature
        Player.ApplySpecies(Player.Species);

        // Reset all growth progress of the player
        Player.ResetGrowth();

        // TODO: HUD animation and loading screen

        if (!CurrentGame!.TutorialState.Enabled)
        {
            // tutorialGUI.EventReceiver?.OnTutorialDisabled();
        }

        // Update state transition triggers
        if (Player.Species.MacroscopicType != previousPlayerStage)
        {
            previousPlayerStage = Player.Species.MacroscopicType;

            if (previousPlayerStage == MacroscopicSpeciesType.Aware)
            {
                // Intentionally not translatable as a placeholder prototype text
                HUD.HUDMessages.ShowMessage(
                    "You are now aware. This prototype has nothing extra yet, please move to the Awakening Stage.",
                    DisplayDuration.ExtraLong);
            }
        }
    }

    public override void OnSuicide()
    {
        Player?.Damage(9999.0f, "suicide");
    }

    public void RotateCamera(float yawMovement, float pitchMovement)
    {
        PlayerCamera.XRotation += pitchMovement;
        PlayerCamera.YRotation += yawMovement;
    }

    /// <summary>
    ///   Temporary land part of the prototype, called from the HUD
    /// </summary>
    public void TeleportToLand()
    {
        if (Player == null)
        {
            GD.PrintErr("Player has disappeared");
            return;
        }

        // Despawn everything except the player
        foreach (Node child in rootOfDynamicallySpawned.GetChildren())
        {
            if (child != Player)
                child.QueueFree();
        }

        // And setup the land "environment"

        // Clear the underwater background
        // TODO: above water panorama backgrounds
        worldEnvironmentNode.Environment = null;

        // Ground plane
        var ground = new StaticBody3D
        {
            PhysicsMaterialOverride = new PhysicsMaterial
            {
                Friction = 1,
                Bounce = 0.1f,
                Absorbent = true,
                Rough = true,
            },
        };

        ground.AddChild(new CollisionShape3D
        {
            Shape = new WorldBoundaryShape3D
            {
                Plane = new Plane(new Vector3(0, 1, 0), 0),
            },
        });

        ground.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(400, 400),
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/environment/Terrain_01_Albedo.png"),
                    NormalEnabled = true,
                    NormalTexture = GD.Load<Texture2D>("res://assets/textures/environment/Terrain_01_Normals.png"),
                    Uv1Scale = new Vector3(42, 42, 42),
                },
            },
        });

        rootOfDynamicallySpawned.AddChild(ground);

        // A not super familiar (different than underwater) rock strewn around for reference
        var rockResource = SimulationParameters.Instance.GetWorldResource("rock");
        var resourceScene = SpawnHelpers.LoadResourceEntityScene();

        foreach (var position in new[]
                 {
                     new Vector3(10, 0, 5),
                     new Vector3(15, 0, 5),
                     new Vector3(10, 0, 8),
                     new Vector3(-3, 0, 5),
                     new Vector3(-8, 0, 6),
                     new Vector3(18, 0, 11),
                     new Vector3(38, 0, 11),
                     new Vector3(-15, 0, 10),
                     new Vector3(-15, 0, -15),
                     new Vector3(-25, 0, -15),
                     new Vector3(-35, 0, -15),
                     new Vector3(25, 0, -5),
                     new Vector3(29, 0, -5),
                     new Vector3(32, 0, -5),
                     new Vector3(35, 0, 5),
                 })
        {
            // But create it as a resource entity so that it can be interacted with
            SpawnHelpers.SpawnResourceEntity(rockResource, new Transform3D(Basis.Identity, position),
                rootOfDynamicallySpawned, resourceScene, true);
        }

        // Placeholder trees
        var treeScene = GD.Load<PackedScene>("res://assets/models/Tree01.tscn");

        foreach (var position in new[]
                 {
                     new Vector3(15, 0, 9),
                     new Vector3(25, 0, 35),
                     new Vector3(50, 0, 10),
                     new Vector3(-30, 0, 5),
                     new Vector3(18, 0, -20),
                     new Vector3(-48, 0, 27),
                 })
        {
            // TODO: proper interactable plants, this is a temporary manually created placeholder tree
            var tree = treeScene.Instantiate<PlaceholderTree>();

            rootOfDynamicallySpawned.AddChild(tree);
            tree.GlobalTransform =
                new Transform3D(new Basis(new Quaternion(new Vector3(0, 1, 0), MathF.PI * random.NextSingle())),
                    position);

            tree.AddToGroup(Constants.INTERACTABLE_GROUP);
        }

        // Modify player state for being on land
        Player.MovementMode = MovementMode.Walking;

        if (Player.Position.Y <= 0)
        {
            Player.Position = new Vector3(Player.Position.X, 0.1f, Player.Position.Z);
        }

        // Modify the player species to be on land
        Player.Species.ReproductionLocation = ReproductionLocation.Land;

        // Fade back in after the "teleport"
        TransitionManager.Instance.AddSequence(ScreenFade.FadeType.FadeIn, 0.3f, null, false);
    }

    public void MoveToAwakeningStage()
    {
        if (Player == null)
            return;

        GD.Print("Moving player to awakening stage prototype");

        Player.Species.MovePlayerToAwakenedStatus();

        // TODO: implement an "inspect" action for inspecting world objects that can unlock primitive
        // technologies
        // For now just add some default unlocks for the prototype

        CurrentGame!.TechWeb.UnlockTechnology(SimulationParameters.Instance.GetTechnology("simpleStoneTools"));

        // TODO: proper society center unlock conditions
        CurrentGame.TechWeb.UnlockTechnology(SimulationParameters.Instance.GetTechnology("societyCenter"));

        // Intentionally not translated prototype message
        HUD.HUDMessages.ShowMessage(
            "You are now in the Awakening Stage prototype. You can now interact with more world objects. " +
            "Pick up rocks to craft an axe to get resources to build a Society Center to advance.",
            DisplayDuration.ExtraLong);

        // Music is different in the awakening stage (and we don't visit the editor here so we need to trigger a music
        // change here)
        StartMusic();
    }

    public void AttemptPlayerWorldInteraction()
    {
        if (PauseManager.Instance.Paused)
            return;

        // TODO: we might in the future have somethings that an aware creature can interact with
        if (Player == null || Player.Species.MacroscopicType != MacroscopicSpeciesType.Awakened)
            return;

        if (interactionPopup.SelectCurrentOptionIfOpen())
            return;

        var target = interactableSystem.GetInteractionTarget();

        if (target == null)
        {
            // Did not find anything for the player to interact with
            HUD.HUDMessages.ShowMessage(Localization.Translate("NOTHING_TO_INTERACT_WITH"), DisplayDuration.Short);
            return;
        }

        // Show interaction context menu for the player to do something with the target
        interactionPopup.ShowForInteractable(target, Player.CalculatePossibleActions(target));

        // TODO: somehow refresh the inventory screen if it is open and the player decided to do a pick up action
    }

    public void PerformBuildOrOpenMenu()
    {
        if (Player == null || Player.Species.MacroscopicType != MacroscopicSpeciesType.Awakened)
            return;

        if (Player.IsPlacingStructure)
        {
            if (PauseManager.Instance.Paused)
                return;

            Player.AttemptStructurePlace();
            return;
        }

        if (pauseMenu.Visible)
            return;

        selectBuildingPopup.OpenWithStructures(CurrentGame!.TechWeb.GetAvailableStructures(), Player, Player);

        // TODO: when a structure is being placed, should we have some kind of indicator on screen what to press to
        // cancel?
    }

    public bool CancelBuildingPlaceIfInProgress()
    {
        if (Player?.IsPlacingStructure != true)
        {
            return false;
        }

        Player.CancelStructurePlacing();
        return true;
    }

    public bool TogglePlayerInventory()
    {
        if (Player == null)
            return false;

        if (HUD.IsInventoryOpen)
        {
            HUD.CloseInventory();
            return true;
        }

        if (pauseMenu.Visible)
            return false;

        try
        {
            // Refresh the items on the ground near the player to show in the inventory screen
            var groundObjects = interactableSystem.GetAllNearbyObjects();

            // Filter to only carriable objects to not let the player to pick up trees and stuff
            HUD.OpenInventory(Player, groundObjects.Where(o => o.CanBeCarried));
        }
        catch (Exception e)
        {
            GD.PrintErr($"Problem trying to show inventory: {e}");
            return false;
        }

        return true;
    }

    public void OnSocietyFounded(PlacedStructure societyCenter)
    {
        if (Player == null || movingToSocietyStage || MovingToEditor)
            return;

        GD.Print("Move to society stage triggered");
        HUD.HUDMessages.ShowMessage(Localization.Translate("MOVING_TO_SOCIETY_STAGE"), DisplayDuration.Long);
        movingToSocietyStage = true;

        // Show cursor while we are switching
        MouseCaptureManager.ReportOpenCapturePrevention(STAGE_TRANSITION_MOUSE_LOCK);

        // Unset the player to disallow doing this multiple times in a row and to disable the player
        var moveCreatureToSocietyCenter = Player;
        Player = null;

        // TODO: actual collisions and moving to the door instead of this
        // TODO: we do a dirty hack here just for the prototype to be simple as the structure root doesn't currently
        // have a collision set on it
        moveCreatureToSocietyCenter.AddCollisionExceptionWith(societyCenter.FirstDescendantOfType<CollisionObject3D>());

        var creatureToCenterVector = societyCenter.GlobalPosition - moveCreatureToSocietyCenter.GlobalPosition;
        creatureToCenterVector = creatureToCenterVector.Normalized();

        // Do an inverse transform to get the vector in creature local space and multiply it to not make the creature
        // move at full speed
        // TODO: this math doesn't seem to be correct
        var wantedMovementDirection = creatureToCenterVector * moveCreatureToSocietyCenter.Transform.Basis;
        wantedMovementDirection.Y = 0;

        // Tweak down once player max move speed is increased again
        float speed = 1;
        wantedMovementDirection = wantedMovementDirection.Normalized() * speed;
        moveCreatureToSocietyCenter.MovementDirection = wantedMovementDirection;

        // TODO: despawn moveCreatureToSocietyCenter once it reaches inside the society center

        // Start the transition to the next stage and a camera animation
        animationEndCameraLookPoint = societyCenter.GlobalPosition;
        animationEndCameraLookPoint += societyCenter.RotatedExtraInteractionOffset() ?? Vector3.Zero;
        firstSocietyCenterTransform = societyCenter.GlobalTransform;

        // Prevent inputs to not allow messing with the camera animation
        PlayerCamera.AllowPlayerInput = false;
        PlayerCamera.FollowedNode = null;

        // Steal the camera from the normal camera holder (this is done to not need to modify the camera passed to the
        // various other systems)
        animationCamera = PlayerCamera.CameraNode;

        moveToSocietyTimer = 0;
        societyCameraAnimationStart = animationCamera.GlobalTransform;
        societyCameraAnimationEnd = StrategicCameraHelpers.CalculateCameraPosition(animationEndCameraLookPoint, 1);

        // Detach from the previous place to not have the arm etc. control nodes apply to it anymore
        animationCamera.ReParent(rootOfDynamicallySpawned);
    }

    public void UpdateBackgroundPanorama()
    {
        if (CurrentGame?.GameWorld.Map.CurrentPatch != null)
        {
            // Panoramas don't exist yet when above water so we need this null check
            if (worldEnvironmentNode.Environment != null)
            {
                var sky = worldEnvironmentNode.Environment.Sky;
                var skyMaterial = (PanoramaSkyMaterial)sky.SkyMaterial;

                skyMaterial.Panorama =
                    GD.Load<Texture2D>(CurrentGame.GameWorld.Map.CurrentPatch.BiomeTemplate.Panorama);
            }
        }

        UpdateAmbientLight();
    }

    public void UpdateAmbientLight()
    {
        if (CurrentGame?.GameWorld.Map.CurrentPatch != null)
        {
            worldLightNode.LightColor = CurrentGame.GameWorld.Map.CurrentPatch.BiomeTemplate.Sunlight.Colour;
        }
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        // if (!IsLoadedFromSave)
        //     spawner.Init();

        ProceduralDataCache.Instance.OnEnterState(MainGameState.MacroscopicStage);

        CurrentGame!.TechWeb.OnTechnologyUnlockedHandler += ShowTechnologyUnlockMessage;

        // TODO: implement
        if (!IsLoadedFromSave)
        {
            // If this is a new game (first time entering the stage), start the camera in top down view
            // as a learning tool
            if (!CurrentGame.IsBoolSet("played_multicellular"))
            {
                CurrentGame.SetBool("played_multicellular", true);

                // TODO: change the view
            }

            // TODO: remove (this is just temporary prototype code)
            // Spawn a chunk to give the player some navigation reference

            var rigidBody = new RigidBody3D
            {
                AxisLockLinearY = true,
            };

            var owner = rigidBody.CreateShapeOwner(rigidBody);
            rigidBody.ShapeOwnerAddShape(owner, new SphereShape3D
            {
                Radius = 10,
            });

            rigidBody.Mass = 100;

            var visualsParent = new Node3D();
            rigidBody.AddChild(visualsParent);

            visualsParent.AddChild(GD.Load<PackedScene>("res://assets/models/Iron5.tscn").Instantiate<Node3D>());

            rigidBody.Position = new Vector3(3, 0, -15);

            rootOfDynamicallySpawned.AddChild(rigidBody);
        }

        // patchManager.CurrentGame = CurrentGame;

        if (IsLoadedFromSave)
        {
            UpdatePatchSettings();
        }
    }

    protected override void OnGameStarted()
    {
        // patchManager.CurrentGame = CurrentGame;

        UpdatePatchSettings();

        SpawnPlayer();
    }

    protected override Node3D CreateGraphicsPreloadNode()
    {
        var result = new Node3D();

        // Calculate a point forwards from the camera
        if (PlayerCamera.CameraNode.IsCurrent() || animationCamera == null)
        {
            PlayerCamera.CameraNode.AddChild(result);
            result.Position = MathUtils.CalculateCameraVisiblePosition(PlayerCamera.CameraNode);
        }
        else
        {
            animationCamera.AddChild(result);
            result.Position = MathUtils.CalculateCameraVisiblePosition(animationCamera);
        }

        return result;
    }

    protected override void UpdatePatchSettings(bool promptPatchNameChange = true)
    {
        // TODO: would be nice to skip this if we are loading a save made in the editor as this gets called twice when
        // going back to the stage
        // if (patchManager.ApplyChangedPatchSettingsIfNeeded(GameWorld.Map.CurrentPatch!))
        // {
        if (promptPatchNameChange)
            HUD.ShowPatchName(CurrentPatchName.ToString());

        // }

        HUD.UpdateEnvironmentalBars(GameWorld.Map.CurrentPatch!.Biome);

        UpdateBackgroundPanorama();
    }

    protected override void OnGameContinuedAsSpecies(Species newPlayerSpecies, Patch inPatch)
    {
        base.OnGameContinuedAsSpecies(newPlayerSpecies, inPatch);

        // TODO: update spawners here when those are added for this stage
        _ = inPatch;
    }

    protected override void SpawnPlayer()
    {
        // Don't want to respawn the player when moving to the society stage
        // Once the flag is reset to false, we'll have the flag for going to the editor true and will not spawn
        // the player thanks to that, so we don't need to add that second check here
        if (HasPlayer || movingToSocietyStage)
            return;

        Player = SpawnHelpers.SpawnCreature(GameWorld.PlayerSpecies, new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMulticellularScene(), false, dummySpawner, CurrentGame!);

        Player.OnDeath = OnPlayerDied;

        Player.OnReproductionStatus = OnPlayerReproductionStatusChanged;

        Player.RequestCraftingInterfaceFor = OnOpenCraftingInterfaceFor;

        PlayerCamera.FollowedNode = Player;

        // spawner.DespawnAll();

        if (spawnedPlayer)
        {
            // Random location on respawn
            // Player.Translation = new Vector3(
            //     random.Next(Constants.MIN_SPAWN_DISTANCE, Constants.MAX_SPAWN_DISTANCE), 0,
            //     random.Next(Constants.MIN_SPAWN_DISTANCE, Constants.MAX_SPAWN_DISTANCE));

            // spawner.ClearSpawnCoordinates();
        }

        spawnedPlayer = true;
        playerRespawnTimer = Constants.PLAYER_RESPAWN_TIME;
    }

    protected override void OnLightLevelUpdate()
    {
    }

    protected override void AutoSave()
    {
        SaveHelper.ShowErrorAboutPrototypeSaving(this);
    }

    protected override void PerformQuickSave()
    {
        SaveHelper.ShowErrorAboutPrototypeSaving(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            interactionPopup.OnInteractionSelectedHandler -= ForwardInteractionSelectionToPlayer;

            if (CurrentGame != null)
                CurrentGame.TechWeb.OnTechnologyUnlockedHandler -= ShowTechnologyUnlockMessage;
        }

        base.Dispose(disposing);
    }

    private void SaveGame(string name)
    {
        // TODO: saving for this stage
        _ = name;
        SaveHelper.ShowErrorAboutPrototypeSaving(this);
    }

    private void OnFinishLoading()
    {
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerDied(MacroscopicCreature player)
    {
        HandlePlayerDeath();

        PlayerCamera.FollowedNode = null;
        Player = null;
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerReproductionStatusChanged(MacroscopicCreature player, bool ready)
    {
        OnCanEditStatusChanged(ready);
    }

    private void ForwardInteractionSelectionToPlayer(IInteractableEntity entity, InteractionType interactionType)
    {
        if (Player == null)
            return;

        if (!Player.AttemptInteraction(entity, interactionType))
            GD.Print("Player couldn't perform the selected action");
    }

    [DeserializedCallbackAllowed]
    private void OnOpenCraftingInterfaceFor(MacroscopicCreature player, IInteractableEntity target)
    {
        if (!TogglePlayerInventory())
        {
            GD.Print("Couldn't open player inventory to then select a craftable resource");
            return;
        }

        HUD.SelectItemForCrafting(target);
    }

    private void ShowTechnologyUnlockMessage(Technology technology)
    {
        HUD.HUDMessages.ShowMessage(Localization.Translate("TECHNOLOGY_UNLOCKED_NOTICE").FormatSafe(technology.Name),
            DisplayDuration.Long);
    }

    private void SwitchToSocietyScene()
    {
        var societyStage = SceneManager.Instance.LoadScene(MainGameState.SocietyStage).Instantiate<SocietyStage>();
        societyStage.CurrentGame = CurrentGame;

        SceneManager.Instance.SwitchToScene(societyStage);

        // Preserve some of the state when moving to the stage for extra continuity
        societyStage.CameraWorldPoint = animationEndCameraLookPoint;

        // TODO: structures should be saved in the world data and not the stage object directly
        var societyCenter = societyStage.AddBuilding(SimulationParameters.Instance.GetStructure("societyCenter"),
            firstSocietyCenterTransform);
        societyCenter.ForceCompletion();

        // Stop explicitly preventing mouse capture (the society stage won't capture the mouse anyway but to not
        // have a pending force no-capture on this is good)
        Invoke.Instance.Queue(() =>
        {
            MouseCaptureManager.ReportClosedCapturePrevention(STAGE_TRANSITION_MOUSE_LOCK);
        });
    }
}
