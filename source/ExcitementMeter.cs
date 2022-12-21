using UnityEngine;
using System;
using System.Collections.Generic;
using SimpleJSON;
using VaMLib;

namespace ThatsLewd
{
  public class ExcitementMeter : MVRScript
  {
    List<object> dynamicUIItems = new List<object>();

    VaMUI.VaMToggle isPlayingToggle;
    VaMUI.VaMSlider excitementMeterSlider;
    JSONStorableAction resetTimerAction;

    UIDynamicInfoText eventInfoText;

    VaMUI.VaMSlider fillTimeSlider;
    VaMUI.VaMSlider fillTimeRandomnessSlider;

    VaMUI.VaMSlider nextEventTimeMinSlider;
    VaMUI.VaMSlider nextEventTimeMaxSlider;

    VaMUI.VaMSlider eventProbabilityFalloffSlider;
    VaMUI.VaMSlider eventMaxAgeSlider;

    VaMUI.EventTrigger onClimaxTrigger;

    float eventTimer = 0f;
    float nextEventTime = 0f;
    float currentFillTime = 0f;
    bool doNextEvent = false;
    bool climaxReached = false;

    float uiUpdateTimer = 69f; // nice
    ExcitementEvent lastEvent = null;

    List<ExcitementEvent> events = new List<ExcitementEvent>();

    public override void Init()
    {
      VaMUI.Init(this, CreateUIElement);
      VaMUI.InitTriggerUtils(this);

      CreateTriggers();
      CreateStaticUI();
      RebuildDynamicUI();
      RecalculateFillTime();
    }

    void OnDestroy()
    {
      VaMUI.Destroy();
      VaMUI.DestroyTriggerUtils();
    }

    void CreateTriggers()
    {
      onClimaxTrigger = VaMUI.CreateEventTrigger("Climax Event");
    }

    void CreateStaticUI()
    {
      isPlayingToggle = VaMUI.CreateToggle("Is Playing", false, callbackNoVal: RecalculateFillTime, register: true);
      isPlayingToggle.Draw(VaMUI.LEFT);

      excitementMeterSlider = VaMUI.CreateSlider("Excitement Meter", 0f, 0f, 1000f, fixedRange: true, integer: true);
      excitementMeterSlider.Draw(VaMUI.LEFT);

      resetTimerAction = VaMUI.CreateAction("Reset Excitement", HandleResetMeter);
      UIDynamicButton resetButton = VaMUI.CreateButton(VaMUI.LEFT, "Reset Excitement", callback: HandleResetMeter, color: VaMUI.YELLOW);

      eventInfoText = VaMUI.CreateInfoText(VaMUI.LEFT, "", 2);
      VaMUI.CreateSpacer(VaMUI.LEFT);

      VaMUI.CreateInfoText(VaMUI.LEFT, "Set the time in seconds for the meter to fill. <b>Fill Time Randomness</b> adds variance to the fill time.", 3);
      fillTimeSlider = VaMUI.CreateSlider("Fill Time", 600f, 0f, 1200f, integer: true, callbackNoVal: RecalculateFillTime, register: true);
      fillTimeSlider.Draw(VaMUI.LEFT);
      fillTimeRandomnessSlider = VaMUI.CreateSlider("Fill Time Randomness", 0.5f, 0f, 1f, callbackNoVal: RecalculateFillTime, register: true);
      fillTimeRandomnessSlider.Draw(VaMUI.LEFT);
      VaMUI.CreateSpacer(VaMUI.LEFT);

      VaMUI.CreateInfoText(VaMUI.LEFT, "Set the min/max time between new events firing. When an event fires, a new event from the right column is triggered based on the current excitement amount.", 5);
      nextEventTimeMinSlider = VaMUI.CreateSlider("Next Event Time Min", 10f, 0f, 60f, register: true);
      nextEventTimeMinSlider.Draw(VaMUI.LEFT);
      nextEventTimeMaxSlider = VaMUI.CreateSlider("Next Event Time Max", 20f, 0f, 60f, register: true);
      nextEventTimeMaxSlider.Draw(VaMUI.LEFT);
      VaMUI.CreateSpacer(VaMUI.LEFT);

      VaMUI.CreateInfoText(
        VaMUI.LEFT,
        "Set the parameters determining how events from the list will be chosen. The rules are as follows:"
        + "\n\n - The <b>Excitement Value</b> of the chosen event must be less than or equal to the current excitement value."
        + "\n\n - Events closer to the current excitement value are more likely to be chosen, with a falloff set by <b>Event Probability Falloff</b>."
        + "\n\n - An event must be within <b>Event Max Age</b> of the current excitement value to be chosen.",
        16
      );
      eventProbabilityFalloffSlider = VaMUI.CreateSlider("Event Probability Falloff", 0.5f, 0f, 1f, fixedRange: true, register: true);
      eventProbabilityFalloffSlider.Draw(VaMUI.LEFT);

      eventMaxAgeSlider = VaMUI.CreateSlider("Event Max Age", 500f, 0f, 1000f, fixedRange: true, integer: true, register: true);
      eventMaxAgeSlider.Draw(VaMUI.LEFT);
      VaMUI.CreateSpacer(VaMUI.LEFT);

      VaMUI.CreateInfoText(
        VaMUI.LEFT,
        "The main idea behind the system is that the excitement is always increasing, and events closer to the current excitement are more likely to be triggered. However, there is always a chance to trigger less exciting events as a way to keep things interesting.",
        7
      );
      VaMUI.CreateSpacer(VaMUI.LEFT, 50f);


      VaMUI.CreateButtonPair(
        VaMUI.RIGHT,
        "Add Event", HandleAddEvent,
        "Sort Events", HandleSortEvents,
        leftColor: VaMUI.GREEN, rightColor: VaMUI.YELLOW
      );
      VaMUI.CreateButton(VaMUI.RIGHT, "Assign Climax Event", callback: onClimaxTrigger.OpenPanel, color: VaMUI.BLUE);
      VaMUI.CreateSpacer(VaMUI.RIGHT);
    }

    void RebuildDynamicUI()
    {
      VaMUI.RemoveUIElements(ref dynamicUIItems);
      for (int i = 0; i < events.Count; i++)
      {
        ExcitementEvent excitementEvent = events[i];

        dynamicUIItems.Add(VaMUI.CreateLabelWithX(VaMUI.RIGHT, $"Event {i + 1}", () => { HandleRemoveEvent(excitementEvent); }));
        dynamicUIItems.Add(VaMUI.CreateButtonPair(
          VaMUI.RIGHT,
          "Assign Action", excitementEvent.action.OpenPanel,
          "Duplicate Event", () => { HandleDuplicateEvent(excitementEvent); },
          leftColor: VaMUI.BLUE, rightColor: VaMUI.GREEN
        ));
        dynamicUIItems.Add(excitementEvent.excitementValueSlider.Draw(VaMUI.RIGHT));
        dynamicUIItems.Add(VaMUI.CreateSpacer(VaMUI.RIGHT));
      }
    }

    void HandleResetMeter()
    {
      excitementMeterSlider.val = 0f;
      QueueNextEvent();
    }

    void HandleAddEvent()
    {
      ExcitementEvent newEvent = new ExcitementEvent();
      events.Add(newEvent);
      RebuildDynamicUI();
    }

    void HandleDuplicateEvent(ExcitementEvent source)
    {
      ExcitementEvent newEvent = source.Clone();
      events.Add(newEvent);
      RebuildDynamicUI();
    }

    void HandleRemoveEvent(ExcitementEvent target)
    {
      events.Remove(target);
      RebuildDynamicUI();
    }

    void HandleSortEvents()
    {
      events.Sort((a, b) =>
      {
        if (a.excitementValueSlider.val < b.excitementValueSlider.val) return -1;
        if (a.excitementValueSlider.val > b.excitementValueSlider.val) return 1;
        return 0;
      });
      RebuildDynamicUI();
    }

    void RemoveAllEvents()
    {
      foreach (var target in events)
      {
        target.action.Remove();
      }
      events.Clear();
    }

    public void Update()
    {
      uiUpdateTimer += Time.deltaTime;
      if (uiUpdateTimer > 3f / 60f)
      {
        uiUpdateTimer = 0f;
        UpdateEventInfoUI();
      }

      if (!isPlayingToggle.val)
      {
        return;
      }

      if (excitementMeterSlider.val >= 1000f)
      {
        if (!climaxReached)
        {
          onClimaxTrigger.Trigger();
          climaxReached = true;
        }
        return;
      }
      else if (climaxReached)
      {
        climaxReached = false;
      }

      eventTimer += Time.deltaTime;

      if (eventTimer > nextEventTime)
      {
        QueueNextEvent();
      }

      if (doNextEvent)
      {
        DoNextEvent();
        RecalculateFillTime();
      }

      float rate = Time.deltaTime * 1000f / currentFillTime;
      excitementMeterSlider.val += rate;
    }

    void UpdateEventInfoUI()
    {
      string lastEventString = "";
      string lastEventName = "";
      if (lastEvent == null)
      {
        lastEventName = "<none>";
      }
      else
      {
        int i = events.FindIndex((t) => t.Equals(lastEvent));
        lastEventName = $"Event {i + 1}";
      }
      lastEventString = $"<b>Last Event:</b> {lastEventName}";
      string nextEventTimeString = $"<b>Next Event Time</b>: {eventTimer:F1}s / {nextEventTime:F1}s";
      eventInfoText.text.text = $"{lastEventString}\n{nextEventTimeString}";
    }

    void QueueNextEvent()
    {
      doNextEvent = true;
    }

    void DoNextEvent()
    {
      doNextEvent = false;
      nextEventTime = UnityEngine.Random.Range(nextEventTimeMinSlider.val, nextEventTimeMaxSlider.val);
      eventTimer = 0f;

      float currentVal = excitementMeterSlider.val;

      List<ExcitementEvent> potentialTriggers = new List<ExcitementEvent>();
      float totalWeight = 0f;

      foreach (var excitementEvent in events)
      {
        float eventVal = excitementEvent.excitementValueSlider.val;
        if (eventVal > currentVal || currentVal - eventVal > eventMaxAgeSlider.val)
        {
          continue;
        }
        totalWeight += excitementEvent.GetCompensatedWeight(currentVal, eventProbabilityFalloffSlider.val);
        potentialTriggers.Add(excitementEvent);
      }
      potentialTriggers.Sort((a, b) =>
      {
        if (a.excitementValueSlider.val < b.excitementValueSlider.val) return -1;
        if (a.excitementValueSlider.val > b.excitementValueSlider.val) return 1;
        return 0;
      });

      float r = UnityEngine.Random.Range(0f, totalWeight);
      ExcitementEvent selectedTrigger = null;

      float w = 0f;
      foreach (var excitementEvent in potentialTriggers)
      {
        w += excitementEvent.GetCompensatedWeight(currentVal, eventProbabilityFalloffSlider.val);
        if (w >= r)
        {
          selectedTrigger = excitementEvent;
          break;
        }
      }

      lastEvent = selectedTrigger;
      if (selectedTrigger != null)
      {
        selectedTrigger.action.Trigger();
      }
    }

    void RecalculateFillTime()
    {
      float r = UnityEngine.Random.Range(0f, fillTimeRandomnessSlider.val) + 1f;
      currentFillTime = fillTimeSlider.val * r;
    }

    public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
    {
      JSONClass json = base.GetJSON(includePhysical, includeAppearance, forceStore);
      this.needsStore = true;

      onClimaxTrigger.StoreJSON(json);

      JSONArray eventsJSON = new JSONArray();
      foreach (var excitementEvent in events)
      {
        JSONClass eventJSON = new JSONClass();
        excitementEvent.StoreJSON(eventJSON);
        eventsJSON.Add(eventJSON);
      }

      json["Events"] = eventsJSON;

      return json;
    }

    public override void LateRestoreFromJSON(JSONClass json, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
    {
      base.LateRestoreFromJSON(json, restorePhysical, restoreAppearance, setMissingToDefault);
      if (json["id"]?.Value != this.storeId) return; // make sure this data is our plugin

      onClimaxTrigger.RestoreFromJSON(json);

      RemoveAllEvents();
      JSONArray eventsJSON = json["Events"].AsArray;
      for (int i = 0; i < eventsJSON.Count; i++)
      {
        JSONClass eventJSON = eventsJSON[i].AsObject;
        ExcitementEvent newEvent = new ExcitementEvent();
        newEvent.RestoreFromJSON(eventJSON);
        events.Add(newEvent);
      }

      RebuildDynamicUI();
    }

    class ExcitementEvent
    {
      public VaMUI.VaMSlider excitementValueSlider;
      public VaMUI.EventTrigger action;

      public ExcitementEvent()
      {
        excitementValueSlider = VaMUI.CreateSlider("Excitement Value", 0f, 0f, 1000f, fixedRange: true, integer: true);
        action = VaMUI.CreateEventTrigger("Event Action");
      }

      public ExcitementEvent Clone()
      {
        ExcitementEvent newEvent = new ExcitementEvent();
        newEvent.excitementValueSlider.valNoCallback = excitementValueSlider.val;
        newEvent.action = action.Clone();
        return newEvent;
      }

      public float GetCompensatedWeight(float excitement, float decay)
      {
        float scaledDecay = 1000f * decay;
        if (scaledDecay == 0) return 0;
        // https://www.desmos.com/calculator/8rwoipduys
        return Mathf.Clamp01(Mathf.Pow(2f, -(excitement - excitementValueSlider.val) / scaledDecay));
      }

      public void StoreJSON(JSONClass json)
      {
        excitementValueSlider.StoreJSON(json);
        action.StoreJSON(json);
      }

      public void RestoreFromJSON(JSONClass json)
      {
        excitementValueSlider.RestoreFromJSON(json);
        action.RestoreFromJSON(json);
      }
    }
  }
}
