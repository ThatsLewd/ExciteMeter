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
    public float weight;
    public EventTrigger action;

    public float GetCompensatedWeight(float excitement)
    {
      if (excitement == 0 || weight == 0) return 0;
      // https://www.desmos.com/calculator/jdchiio9rm
      float diff = 1f - (excitement - value) / 1000f;
      diff = Mathf.Pow(diff, 5f);
      return (1 - diff) * Mathf.Pow(0.8f * weight, 3f) + diff;
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
    JSONStorableFloat maxExciteDiffStorable;

    float timer = 0f;
    float currentFillTime = 0f;
    float nextTriggerTime = 0f;

    List<TriggerBreakpoint> triggers = new List<TriggerBreakpoint>();
    List<object> triggerMenuItems = new List<object>();

    public override void Init()
    {
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
      UIBuilder.CreateSliderFloat(out exciteMeterStorable, "Excite Meter", 0f, 0f, 1000f, false, false);
      UIBuilder.CreateAction(out resetTimerActionStorable, "Reset Meter", HandleResetTimer);
      UIDynamicButton resetButton = UIBuilder.CreateButton("Reset Meter", HandleResetTimer, false);
      resetButton.buttonColor = UIBuilder.UIColor.YELLOW;
      UIBuilder.CreateSpacer(20f, false);

      UIBuilder.CreateSliderFloatWithRange(out fillTimeStorable, "Fill Time", 300f, 0f, 1200f, false);
      UIBuilder.CreateSliderFloat(out fillVarianceStorable, "Fill Variance", 0.5f, 0f, 1f, false);

      UIBuilder.CreateSliderFloatWithRange(out triggerTimeMinStorable, "Trigger Time Min", 10f, 0f, 60f, false);
      UIBuilder.CreateSliderFloatWithRange(out triggerTimeMaxStorable, "Trigger Time Max", 20f, 0f, 60f, false);

      UIBuilder.CreateSliderInt(out maxExciteDiffStorable, "Max Excitement Diff", 500f, 0f, 1000f, false);

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

        UIDynamicButton assignButton = UIBuilder.CreateButton("Assign Trigger", trigger.action.OpenPanel, true);
        assignButton.buttonColor = UIBuilder.UIColor.BLUE;
        triggerMenuItems.Add(assignButton);

        JSONStorableFloat valueStorable;
        UIDynamicSlider valueSlider = UIBuilder.CreateSliderInt(out valueStorable, "Excitement Value", 0f, 0f, 1000f, true, false);
        valueStorable.valNoCallback = trigger.value;
        valueStorable.setCallbackFunction += (float val) => { trigger.value = val; };
        triggerMenuItems.Add(valueSlider);

        JSONStorableFloat weightStorable;
        UIDynamicSlider weightSlider = UIBuilder.CreateSliderFloat(out weightStorable, "Weight", 1f, 0f, 1f, true, false);
        weightStorable.valNoCallback = trigger.weight;
        weightStorable.setCallbackFunction += (float val) => { trigger.weight = val; };
        triggerMenuItems.Add(weightSlider);

        UIDynamic spacer = UIBuilder.CreateSpacer(20f, true);
        triggerMenuItems.Add(spacer);
      }
    }

    void HandleResetTimer()
    {
      exciteMeterStorable.val = 0f;
    }

    void HandleAddTrigger()
    {
      TriggerBreakpoint trigger = new TriggerBreakpoint()
      {
        value = 0f,
        weight = 1f,
        action = new EventTrigger(this, "Trigger Action"),
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
      if (!playingStorable.val) return;

      timer += Time.deltaTime;

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
        totalWeight += trigger.GetCompensatedWeight(exciteMeterStorable.val);
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
        w += trigger.GetCompensatedWeight(exciteMeterStorable.val);
        if (w >= r)
        {
          selectedTrigger = trigger;
          break;
        }
      }

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

        JSONArray triggersJSON = new JSONArray();
        foreach (var trigger in triggers)
        {
          JSONClass triggerJSON = new JSONClass();
          triggerJSON["Value"].AsFloat = trigger.value;
          triggerJSON["Weight"].AsFloat = trigger.weight;
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
        RemoveAllTriggers();

        JSONArray triggersJSON = json["Triggers"].AsArray;
        for (int i = 0; i < triggersJSON.Count; i++)
        {
          JSONClass triggerJSON = triggersJSON[i].AsObject;
          TriggerBreakpoint trigger = new TriggerBreakpoint()
          {
            value = triggerJSON["Value"].AsFloat,
            weight = triggerJSON["Weight"].AsFloat,
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
