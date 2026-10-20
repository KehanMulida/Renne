using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Attach only to the title bar; item slots keep their original drag handlers.</summary>
public class InventoryPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private InventoryCanvas inventoryCanvas;
    private Vector2 pointerStart, panelStart;

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || panel == null) return;
        inventoryCanvas?.BeginPanelDrag();
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)panel.parent,e.position,e.pressEventCamera,out pointerStart);
        panelStart=panel.anchoredPosition;
    }
    public void OnDrag(PointerEventData e)
    {
        if(e.button != PointerEventData.InputButton.Left || panel == null) return;
        var parent=(RectTransform)panel.parent;
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,e.position,e.pressEventCamera,out var pointer))return;
        panel.anchoredPosition=panelStart+pointer-pointerStart;
        // Clamp all four edges in canvas coordinates, including under CanvasScaler.
        var corners=new Vector3[4]; panel.GetWorldCorners(corners);
        Vector2 min=parent.InverseTransformPoint(corners[0]), max=parent.InverseTransformPoint(corners[2]);
        var bounds=parent.rect; var correction=Vector2.zero;
        if(min.x<bounds.xMin)correction.x=bounds.xMin-min.x;
        else if(max.x>bounds.xMax)correction.x=bounds.xMax-max.x;
        if(min.y<bounds.yMin)correction.y=bounds.yMin-min.y;
        else if(max.y>bounds.yMax)correction.y=bounds.yMax-max.y;
        panel.anchoredPosition+=correction;
        inventoryCanvas?.RememberPanelPosition();
    }
    public void OnEndDrag(PointerEventData e) { inventoryCanvas?.RememberPanelPosition(); }
}
