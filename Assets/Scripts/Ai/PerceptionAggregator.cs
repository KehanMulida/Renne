using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;


public class PerceptionAggregator
{
    private List<IPerceptionModule> modules = new List<IPerceptionModule>();
    public event Action<PerceptionEvent> OnAnyPerception;

    public void RegisterModule(IPerceptionModule module)
    {
        modules.Add(module);
        module.OnPerceptionEvent += HandlePerceptionEvent;
      //  Debug.Log($"[PerceptionAggregator] Module registered, event subscribed");
    }

    public void UpdateAll()
    {
       // Debug.Log($"[PerceptionAggregator] UpdateAll called, modules: {modules.Count}");
        
        foreach (var module in modules)
        {
            module.UpdatePerception();
        }
    }

    private void HandlePerceptionEvent(PerceptionEvent evt)
    {
       // Debug.Log($"[PerceptionAggregator] Event received: {evt.Type}");
        OnAnyPerception?.Invoke(evt);
    }
}