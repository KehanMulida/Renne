using System.Collections;
using UnityEngine;

public class InvestigationExecutor : IActionExecutor
{
    private Transform owner;
    private EnemyConfig config;
    private UnitMovement unitMovement;

    public void Initialize(Transform owner, EnemyConfig config)
    {
        this.owner = owner;
        this.config = config;
        this.unitMovement = owner.GetComponent<UnitMovement>();
    }

    public bool CanExecute() => unitMovement != null && !unitMovement.IsMoving;

    public IEnumerator Execute(ActionContext context)
    {
        Vector2Int targetGrid = GridManager.Instance.WorldToGrid(context.TargetPosition);
        unitMovement.MoveToGrid(targetGrid, context.TargetFloor != -1 ? context.TargetFloor : unitMovement.CurrentFloor);
        
        while (unitMovement.IsMoving)
        {
            yield return null;
        }
        
        yield return new WaitForSeconds(config.investigationDuration);
    }
}