using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using SimpleJSON;
using MacGruber;

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
      onClimaxTrigger = new EventTrigger(this, "OnClimax Trigger");
      CreateUI();
    }

    void OnDestroy()
    {
      UIBuilder.Destroy();
    }

    void CreateUI()
    {
      SimpleTriggerHandler.LoadAssets();
      UIBuilder.Init(this, CreateUIElement);

      UIBuilder.CreateToggle(out playingStorable, "Is Playing", false, false);
      UIBuilder.CreateSliderInt(out exciteMeterStorable, "Excitement Meter", 0f, 0f, 1000f, false, false);
      UIBuilder.CreateAction(out resetTimerActionStorable, "Reset Excitement", HandleResetMeter);
      UIDynamicButton resetButton = UIBuilder.CreateButton("Reset Excitement", HandleResetMeter, false);
      resetButton.buttonColor = UIBuilder.UIColor.YELLOW;
      UIBuilder.CreateSpacer(20f, false);

      stateInfo = new JSONStorableString("StateInfo", "");
      UIDynamic stateInfoTextField = CreateTextField(stateInfo, false);
      stateInfoTextField.height = 80f;

      UIBuilder.CreateSliderIntWithRange(out fillTimeStorable, "Fill Time", 600f, 0f, 1200f, false);
      fillTimeStorable.setCallbackFunction += (val) => { RecalculateFillTime(); };
      UIBuilder.CreateSliderFloat(out fillVarianceStorable, "Fill Variance", 0.5f, 0f, 1f, false);
      fillVarianceStorable.setCallbackFunction += (val) => { RecalculateFillTime(); };

      UIBuilder.CreateSliderFloatWithRange(out triggerTimeMinStorable, "Trigger Time Min", 10f, 0f, 60f, false);
      UIBuilder.CreateSliderFloatWithRange(out triggerTimeMaxStorable, "Trigger Time Max", 20f, 0f, 60f, false);

      UIBuilder.CreateSliderFloat(out exciteDiffDecayStorable, "Excitement Diff Decay", 0.1f, 0f, 1f, false);
      UIBuilder.CreateSliderInt(out maxExciteDiffStorable, "Max Excitement Diff", 500f, 0f, 1000f, false);

      UIDynamicButton climaxTriggerButton = UIBuilder.CreateButton("Assign Climax Action", onClimaxTrigger.OpenPanel, false);
      climaxTriggerButton.buttonColor = UIBuilder.UIColor.BLUE;

      UIBuilder.CreateTwinButton("Add Trigger", HandleAddTrigger, "Sort Triggers", HandleSortTriggers, true);
      UIBuilder.CreateSpacer(20f, true);
      RebuildTriggersUI();
    }

    void RebuildTriggersUI()
    {
      UIBuilder.RemoveUIElements(triggerMenuItems);
      for (int i = 0; i < triggers.Count; i++)
      {
        var trigger = triggers[i];

        UIDynamicLabelXButton labelX = UIBuilder.CreateLabelXButton($"Trigger {i + 1}", () => { HandleRemoveTrigger(trigger); }, true);
        triggerMenuItems.Add(labelX);

        UIDynamicTwinButton twinButtons = UIBuilder.CreateTwinButton("Assign Action", trigger.action.OpenPanel, "Duplicate Trigger", () => { HandleDuplicateTrigger(trigger); }, true);
        triggerMenuItems.Add(twinButtons);

        JSONStorableFloat valueStorable;
        UIDynamicSlider valueSlider = UIBuilder.CreateSliderInt(out valueStorable, "Excitement Value", 0f, 0f, 1000f, true, false);
        valueStorable.valNoCallback = trigger.value;
        valueStorable.setCallbackFunction += (float val) => { trigger.value = val; };
        triggerMenuItems.Add(valueSlider);

        UIDynamic spacer = UIBuilder.CreateSpacer(20f, true);
        triggerMenuItems.Add(spacer);
      }
    }

    void HandleResetMeter()
    {
      exciteMeterStorable.val = 0f;
    }

    void HandleAddTrigger()
    {
      TriggerBreakpoint trigger = new TriggerBreakpoint()
      {
        value = 0f,
        action = new EventTrigger(this, "Trigger Action"),
      };
      triggers.Add(trigger);
      RebuildTriggersUI();
    }

    void HandleDuplicateTrigger(TriggerBreakpoint fromTrigger)
    {
      TriggerBreakpoint trigger = new TriggerBreakpoint()
      {
        value = fromTrigger.value,
        action = new EventTrigger(fromTrigger.action),
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

    void UpdateStateInfoUI()
    {
      string lastTriggerString;
      if (lastTrigger == null) {
        lastTriggerString = "null";
      } else {
        int i = triggers.FindIndex((t) => t.Equals(lastTrigger));
        lastTriggerString = $"Trigger {i + 1}";
      }
      stateInfo.val = $@"
<color=#000><b>Last Trigger:</b></color> <color=#333>{lastTriggerString}</color>";
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
        nextTriggerTime = timer + Random.Range(triggerTimeMinStorable.val, triggerTimeMaxStorable.val);
        DoNextTrigger();
        RecalculateFillTime();
      }

      float rate = Time.deltaTime * 1000f / currentFillTime;
      exciteMeterStorable.val += rate;
    }

    void DoNextTrigger()
    {
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

        json[onClimaxTrigger.Name] = onClimaxTrigger.GetJSON(base.subScenePrefix);

        JSONArray triggersJSON = new JSONArray();
        foreach (var trigger in triggers)
        {
          JSONClass triggerJSON = new JSONClass();
          triggerJSON["Value"].AsFloat = trigger.value;
          triggerJSON[trigger.action.Name] = trigger.action.GetJSON(base.subScenePrefix);

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
        onClimaxTrigger.RestoreFromJSON(json, base.subScenePrefix, base.mergeRestore, setMissingToDefault);

        RemoveAllTriggers();
        JSONArray triggersJSON = json["Triggers"].AsArray;
        for (int i = 0; i < triggersJSON.Count; i++)
        {
          JSONClass triggerJSON = triggersJSON[i].AsObject;
          TriggerBreakpoint trigger = new TriggerBreakpoint()
          {
            value = triggerJSON["Value"].AsFloat,
            action = new EventTrigger(this, "Trigger Action"),
          };
          trigger.action.RestoreFromJSON(triggerJSON, base.subScenePrefix, base.mergeRestore, setMissingToDefault);
          triggers.Add(trigger);
        }

        RebuildTriggersUI();
      }

    }
  }
}
