using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using SimpleJSON;
using VaMUtils;

namespace ThatsLewd
{
  class TriggerBreakpoint
  {
    public float value;
    public EventTrigger action;

    public float GetCompensatedWeight(float excitement, float decay)
    {
      float scaledDecay = 1000f * decay;
      if (scaledDecay == 0) return 0;
      // https://www.desmos.com/calculator/8rwoipduys
      return Mathf.Clamp01(Mathf.Pow(2f, -(excitement - value) / scaledDecay));
    }
  }

  public class ExciteMeter : MVRScript
  {
    JSONStorableBool playingStorable;
    JSONStorableFloat exciteMeterStorable;
    JSONStorableAction resetTimerActionStorable;
    JSONStorableFloat fillTimeStorable;
    JSONStorableFloat fillVarianceStorable;
    JSONStorableFloat triggerTimeMinStorable;
    JSONStorableFloat triggerTimeMaxStorable;
    JSONStorableFloat exciteDiffDecayStorable;
    JSONStorableFloat maxExciteDiffStorable;

    JSONStorableString stateInfo;

    EventTrigger onClimaxTrigger;

    float timer = 0f;
    float currentFillTime = 0f;
    float nextTriggerTime = 0f;
    bool playingLastFrame = false;

    float lastSlowUpdateTime = 0f;
    TriggerBreakpoint lastTrigger = null;

    List<TriggerBreakpoint> triggers = new List<TriggerBreakpoint>();
    List<object> triggerMenuItems = new List<object>();

    public override void Init()
    {
      TriggerUtil.Init(this);
      UIBuilder.Init(this, CreateUIElement);

      CreateTriggers();
      CreateUI();
    }

    void OnDestroy()
    {
      UIBuilder.Destroy();
      TriggerUtil.Destroy();
    }

    void CreateTriggers()
    {
      onClimaxTrigger = TriggerUtil.Create<EventTrigger>("OnClimax Trigger");
    }

    void CreateUI()
    {
      UIBuilder.CreateToggle(out playingStorable, UIColumn.LEFT, "Is Playing", false);
      UIBuilder.CreateSlider(out exciteMeterStorable, UIColumn.LEFT, "Excitement Meter", 0f, 0f, 1000f, fixedRange: true, integer: true, interactable: false, register: false);
      UIBuilder.CreateAction(out resetTimerActionStorable, "Reset Excitement", HandleResetMeter);
      UIDynamicButton resetButton = UIBuilder.CreateButton(UIColumn.LEFT, "Reset Excitement", HandleResetMeter);
      resetButton.buttonColor = UIColor.YELLOW;
      UIBuilder.CreateSpacer(UIColumn.LEFT);

      stateInfo = new JSONStorableString("StateInfo", "");
      UIDynamic stateInfoTextField = CreateTextField(stateInfo, false);
      stateInfoTextField.height = 80f;

      UIBuilder.CreateSlider(out fillTimeStorable, UIColumn.LEFT, "Fill Time", 900f, 0f, 1200f, integer: true);
      fillTimeStorable.setCallbackFunction += (val) => { RecalculateFillTime(); };
      UIBuilder.CreateSlider(out fillVarianceStorable, UIColumn.LEFT, "Fill Variance", 0.5f, 0f, 1f);
      fillVarianceStorable.setCallbackFunction += (val) => { RecalculateFillTime(); };

      UIBuilder.CreateSlider(out triggerTimeMinStorable, UIColumn.LEFT, "Trigger Time Min", 10f, 0f, 60f);
      UIBuilder.CreateSlider(out triggerTimeMaxStorable, UIColumn.LEFT, "Trigger Time Max", 20f, 0f, 60f);

      UIBuilder.CreateSlider(out exciteDiffDecayStorable, UIColumn.LEFT, "Excitement Diff Decay", 0.5f, 0f, 1f, fixedRange: true);
      UIBuilder.CreateSlider(out maxExciteDiffStorable, UIColumn.LEFT, "Max Excitement Diff", 500f, 0f, 1000f, fixedRange: true, integer: true);

      UIDynamicButton climaxTriggerButton = UIBuilder.CreateButton(UIColumn.LEFT, "Assign Climax Action", onClimaxTrigger.OpenPanel);
      climaxTriggerButton.buttonColor = UIColor.BLUE;

      UIBuilder.CreateTwinButton(UIColumn.RIGHT, "Add Trigger", HandleAddTrigger, "Sort Triggers", HandleSortTriggers);
      UIBuilder.CreateSpacer(UIColumn.RIGHT);
      RebuildTriggersUI();
    }

    void RebuildTriggersUI()
    {
      UIBuilder.RemoveUIElements(ref triggerMenuItems);
      for (int i = 0; i < triggers.Count; i++)
      {
        var trigger = triggers[i];

        UIDynamicLabelXButton labelX = UIBuilder.CreateLabelXButton(UIColumn.RIGHT, $"Trigger {i + 1}", () => { HandleRemoveTrigger(trigger); });
        triggerMenuItems.Add(labelX);

        UIDynamicTwinButton twinButtons = UIBuilder.CreateTwinButton(UIColumn.RIGHT, "Assign Action", trigger.action.OpenPanel, "Duplicate Trigger", () => { HandleDuplicateTrigger(trigger); });
        triggerMenuItems.Add(twinButtons);

        JSONStorableFloat valueStorable;
        UIDynamicSlider valueSlider = UIBuilder.CreateSlider(out valueStorable, UIColumn.RIGHT, "Excitement Value", 0f, 0f, 1000f, fixedRange: true, integer: true);
        valueStorable.valNoCallback = trigger.value;
        valueStorable.setCallbackFunction += (float val) => { trigger.value = val; };
        triggerMenuItems.Add(valueSlider);

        UIDynamic spacer = UIBuilder.CreateSpacer(UIColumn.RIGHT);
        triggerMenuItems.Add(spacer);
      }
    }

    void HandleResetMeter()
    {
      exciteMeterStorable.val = 0f;
      DoNextTrigger();
    }

    void HandleAddTrigger()
    {
      TriggerBreakpoint trigger = new TriggerBreakpoint()
      {
        value = 0f,
        action = TriggerUtil.Create<EventTrigger>("Trigger Action"),
      };
      triggers.Add(trigger);
      RebuildTriggersUI();
    }

    void HandleDuplicateTrigger(TriggerBreakpoint fromTrigger)
    {
      TriggerBreakpoint trigger = new TriggerBreakpoint()
      {
        value = fromTrigger.value,
        action = TriggerUtil.Clone(fromTrigger.action),
      };
      triggers.Add(trigger);
      RebuildTriggersUI();
    }

    void HandleRemoveTrigger(TriggerBreakpoint trigger)
    {
      triggers.Remove(trigger);
      RebuildTriggersUI();
    }

    void HandleSortTriggers()
    {
      triggers.Sort((a, b) =>
      {
        if (a.value < b.value) return -1;
        if (a.value > b.value) return 1;
        return 0;
      });
      RebuildTriggersUI();
    }

    void RemoveAllTriggers()
    {
      foreach (var trigger in triggers)
      {
        trigger.action.Remove();
      }
      triggers.Clear();
    }

    public void Update()
    {
      if (Time.time - lastSlowUpdateTime > 0.5)
      {
        lastSlowUpdateTime = Time.time;
        UpdateStateInfoUI();
      }

      if (!playingStorable.val)
      {
        playingLastFrame = false;
        return;
      }

      if (exciteMeterStorable.val >= 1000f)
      {
        if (playingLastFrame)
        {
          onClimaxTrigger.Trigger();
        }

        playingLastFrame = false;
        return;
      }

      timer += Time.deltaTime;
      playingLastFrame = true;

      if (timer > nextTriggerTime)
      {
        DoNextTrigger();
        RecalculateFillTime();
      }

      float rate = Time.deltaTime * 1000f / currentFillTime;
      exciteMeterStorable.val += rate;
    }

    void UpdateStateInfoUI()
    {
      string lastTriggerString;
      if (lastTrigger == null)
      {
        lastTriggerString = "null";
      }
      else
      {
        int i = triggers.FindIndex((t) => t.Equals(lastTrigger));
        lastTriggerString = $"Trigger {i + 1}";
      }
      stateInfo.val = $@"
<color=#000><b>Last Trigger:</b></color> <color=#333>{lastTriggerString}</color>";
    }

    void DoNextTrigger()
    {
      nextTriggerTime = timer + Random.Range(triggerTimeMinStorable.val, triggerTimeMaxStorable.val);

      List<TriggerBreakpoint> potentialTriggers = new List<TriggerBreakpoint>();
      float totalWeight = 0f;

      foreach (var trigger in triggers)
      {
        if (trigger.value > exciteMeterStorable.val || exciteMeterStorable.val - trigger.value > maxExciteDiffStorable.val)
        {
          continue;
        }
        totalWeight += trigger.GetCompensatedWeight(exciteMeterStorable.val, exciteDiffDecayStorable.val);
        potentialTriggers.Add(trigger);
      }
      potentialTriggers.Sort((a, b) =>
      {
        if (a.value < b.value) return -1;
        if (a.value > b.value) return 1;
        return 0;
      });

      float r = Random.Range(0f, totalWeight);
      TriggerBreakpoint selectedTrigger = null;

      float w = 0f;
      foreach (var trigger in potentialTriggers)
      {
        w += trigger.GetCompensatedWeight(exciteMeterStorable.val, exciteDiffDecayStorable.val);
        if (w >= r)
        {
          selectedTrigger = trigger;
          break;
        }
      }

      lastTrigger = selectedTrigger;
      if (selectedTrigger != null)
      {
        selectedTrigger.action.Trigger();
      }
    }

    void RecalculateFillTime()
    {
      float r = Random.Range(0f, fillVarianceStorable.val) + 1f;
      currentFillTime = fillTimeStorable.val * r;
    }

    public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
    {
      JSONClass json = base.GetJSON(includePhysical, includeAppearance, forceStore);

      if (includePhysical || forceStore)
      {
        needsStore = true;

        json[onClimaxTrigger.name] = onClimaxTrigger.GetJSON(base.subScenePrefix);

        JSONArray triggersJSON = new JSONArray();
        foreach (var trigger in triggers)
        {
          JSONClass triggerJSON = new JSONClass();
          triggerJSON["Value"].AsFloat = trigger.value;
          triggerJSON[trigger.action.name] = trigger.action.GetJSON(base.subScenePrefix);

          triggersJSON.Add(triggerJSON);
        }

        json["Triggers"] = triggersJSON;
      }

      return json;
    }

    public override void LateRestoreFromJSON(JSONClass json, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
    {
      base.LateRestoreFromJSON(json, restorePhysical, restoreAppearance, setMissingToDefault);

      if (!base.physicalLocked && restorePhysical && !IsCustomPhysicalParamLocked("trigger"))
      {
        TriggerUtil.RestoreFromJSON(ref onClimaxTrigger, json, setMissingToDefault);
        
        RemoveAllTriggers();
        JSONArray triggersJSON = json["Triggers"].AsArray;
        for (int i = 0; i < triggersJSON.Count; i++)
        {
          JSONClass triggerJSON = triggersJSON[i].AsObject;
          TriggerBreakpoint trigger = new TriggerBreakpoint()
          {
            value = triggerJSON["Value"].AsFloat,
          };
          TriggerUtil.RestoreFromJSON(out trigger.action, "Trigger Action", json, setMissingToDefault);
          triggers.Add(trigger);
        }

        RebuildTriggersUI();
      }

    }
  }
}
