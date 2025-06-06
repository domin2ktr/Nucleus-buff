﻿using System;
using System.Collections.Generic;
using Godot;

/// <summary>
///   The compounds panel and the agents panel part of the microbe HUD
/// </summary>
public partial class CompoundPanels : BarPanelBase
{
    private readonly StringName vSeparationReference = new("v_separation");
    private readonly StringName hSeparationReference = new("h_separation");

    private readonly List<CompoundProgressBar> agentsCreatedBars = new();

#pragma warning disable CA2213
    [Export]
    private GridContainer agentsContainer = null!;

    [Export]
    private Control agentsParentContainer = null!;
#pragma warning restore CA2213

    private bool showAgents = true;

    // Needed to determine which animation should be played
    private bool currentAgentsState = true;
    private bool currentCompoundsState = true;

    /// <summary>
    ///   Shows / hides the agents panel. Can only be visible if the compounds panel is also visible.
    /// </summary>
    [Export]
    public bool ShowAgents
    {
        get => showAgents;
        set
        {
            if (showAgents == value)
                return;

            showAgents = value;
            UpdatePanelShowAnimation();
        }
    }

    /// <summary>
    ///   If true, extra vertical space is added between items when compressed
    /// </summary>
    [Export]
    public bool ApplyCompressedVerticalExtraSpace { get; set; }

    public override void _Ready()
    {
        base._Ready();

        if (!ShowAgents)
            HideImmediately();
    }

    /// <inheritdoc cref="BarPanelBase.AddPrimaryBar"/>
    public override void AddPrimaryBar(CompoundProgressBar bar)
    {
        base.AddPrimaryBar(bar);

        // When compressed, the column state depends on bar count, so that must be kept up to date
        if (PanelCompressed)
        {
            UpdateCompressedColumnCount();
        }
    }

    /// <summary>
    ///   Add bars to the secondary, agents holder
    /// </summary>
    public void AddAgentBar(CompoundProgressBar agentBar)
    {
        if (expandButton == null)
            throw new InvalidOperationException("Needs to be in tree first");

        if (PanelCompressed)
        {
            agentBar.Compact = true;
            UpdateCompressedColumnCount();
        }

        agentsContainer.AddChild(agentBar);
        agentsCreatedBars.Add(agentBar);
    }

    protected override void HideImmediately()
    {
        if (!ShowPanel)
        {
            base.HideImmediately();
            currentCompoundsState = false;
            currentAgentsState = false;
        }
        else if (!ShowAgents)
        {
            agentsParentContainer.Hide();
            currentAgentsState = false;
        }
    }

    protected override void UpdatePanelState()
    {
        if (expandButton == null)
            return;

        base.UpdatePanelState();

        if (PanelCompressed)
        {
            if (ApplyCompressedVerticalExtraSpace)
                primaryBarContainer.AddThemeConstantOverride(vSeparationReference, 20);

            primaryBarContainer.AddThemeConstantOverride(hSeparationReference, 14);

            UpdateCompressedColumnCount();

            foreach (var bar in primaryBars)
            {
                bar.Compact = true;
            }

            agentsContainer.Columns = 2;

            foreach (var bar in agentsCreatedBars)
            {
                bar.Compact = true;
            }
        }
        else
        {
            primaryBarContainer.Columns = 1;

            if (ApplyCompressedVerticalExtraSpace)
                primaryBarContainer.AddThemeConstantOverride(vSeparationReference, 5);

            primaryBarContainer.AddThemeConstantOverride(hSeparationReference, 0);

            foreach (var bar in primaryBars)
            {
                bar.Compact = false;
            }

            agentsContainer.Columns = 1;

            foreach (var bar in agentsCreatedBars)
            {
                bar.Compact = false;
            }
        }
    }

    protected override void UpdatePanelShowAnimation()
    {
        if (panelHideAnimationPlayer == null)
            return;

        bool needToChangeAgents = currentAgentsState != ShowAgents;
        bool needToChangeCompounds = currentCompoundsState != ShowPanel;

        if (!needToChangeAgents && !needToChangeCompounds)
            return;

        // To not mess up the state, don't start a new animation if we are currently playing, instead try again next
        // frame to eventually get into the correct state
        if (panelHideAnimationPlayer.IsPlaying())
        {
            Invoke.Instance.QueueForObject(UpdatePanelShowAnimation, this);
            return;
        }

        // Determine which of the 6 animations we should play
        // TODO: if there was a way for animation player to allow having the initial animation key frame grab the
        // previous state, that would allow cutting this down
        if (needToChangeCompounds)
        {
            if (ShowPanel)
            {
                if (ShowAgents)
                {
                    panelHideAnimationPlayer.Play("ShowBoth");
                    currentCompoundsState = true;
                    currentAgentsState = true;
                }
                else
                {
                    panelHideAnimationPlayer.Play("ShowOnlyCompounds");
                    currentCompoundsState = true;
                    currentAgentsState = false;
                }
            }
            else
            {
                if (ShowAgents || (needToChangeAgents && !ShowAgents))
                {
                    panelHideAnimationPlayer.Play("HideBoth");
                    currentCompoundsState = false;
                    currentAgentsState = false;
                }
                else
                {
                    panelHideAnimationPlayer.Play("HideOnlyCompounds");
                    currentCompoundsState = false;
                    currentAgentsState = false;
                }
            }
        }
        else if (needToChangeAgents)
        {
            if (ShowAgents)
            {
                panelHideAnimationPlayer.Play("AddAgents");
                currentAgentsState = true;
            }
            else
            {
                panelHideAnimationPlayer.Play("HideAgents");
                currentAgentsState = false;
            }
        }
        else
        {
            GD.PrintErr("Either need to change compounds or agents, both should not be false");
        }

        if (currentAgentsState != ShowAgents || currentCompoundsState != ShowPanel)
        {
            GD.PrintErr($"Panel animation states didn't result in wanted final state. Panel: " +
                $"{currentCompoundsState} != {ShowPanel} (wanted) or {currentAgentsState} != {ShowAgents} (wanted)");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            vSeparationReference.Dispose();
            hSeparationReference.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateCompressedColumnCount()
    {
        if (primaryBars.Count < 4)
        {
            primaryBarContainer.Columns = 2;
        }
        else
        {
            primaryBarContainer.Columns = 3;
        }
    }
}
