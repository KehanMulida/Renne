using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation bindings only. Gameplay remains in the existing controllers.</summary>
public class SurvivalHUD : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private EquipmentManager equipment;
    [SerializeField] private Inventory inventory;
    [SerializeField] private PlayerInputController input;
    [SerializeField] private Image healthFill, sanityFill;
    [SerializeField] private Text healthValue, sanityValue, apValue, turnLabel, inventoryCount;
    [SerializeField] private Text heldItemName, heldItemQuantity;
    [SerializeField] private Image[] apPips;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private Color pipReady = new Color(.68f,.76f,.65f,1);
    [SerializeField] private Color pipSpent = new Color(.12f,.17f,.19f,1);
    private TurnBasedUnit unit;

    void Start()
    {
        if(player==null)player=FindObjectOfType<PlayerController>();
        if(equipment==null && player!=null)equipment=player.GetComponent<EquipmentManager>();
        if(inventory==null && player!=null)inventory=player.GetComponent<Inventory>();
        if(input==null)input=FindObjectOfType<PlayerInputController>();
        if(player!=null)unit=player.GetComponent<TurnBasedUnit>();
        if(endTurnButton!=null)endTurnButton.onClick.AddListener(EndTurn);
        Refresh();
    }
    void OnDestroy() { if(endTurnButton!=null)endTurnButton.onClick.RemoveListener(EndTurn); }
    void LateUpdate() { Refresh(); }
    public void Refresh()
    {
        if(player!=null)
        {
            if(healthFill!=null)healthFill.fillAmount=(float)player.CurrentHp/Mathf.Max(1,player.MaxHp);
            if(sanityFill!=null)sanityFill.fillAmount=(float)player.CurrentSanity/Mathf.Max(1,player.MaxSanity);
            Set(healthValue,$"{player.CurrentHp} / {player.MaxHp}");
            Set(sanityValue,$"{player.CurrentSanity} / {player.MaxSanity}");
            int max=player.GetCurrentAP(), current=unit!=null?unit.RemainingActionPoints:0;
            Set(apValue,$"AP {current} / {max}");
            if(apPips!=null)for(int i=0;i<apPips.Length;i++)
            {
                if(apPips[i]==null)continue;
                apPips[i].gameObject.SetActive(i<max);
                apPips[i].color=i<current?pipReady:pipSpent;
            }
        }
        bool playerTurn=TurnSystem.Instance!=null && TurnSystem.Instance.IsCurrentFaction(TurnFaction.Player);
        Set(turnLabel,playerTurn?"YOUR TURN":"ENEMY TURN");
        if(endTurnButton!=null)endTurnButton.interactable=playerTurn && !TurnSystem.Instance.IsProcessingTurn;
        if(inventory!=null)Set(inventoryCount,$"{inventory.SlotCount} / {inventory.MaxSlots}");
        var held=equipment!=null?equipment.CurrentSlot:null;
        Set(heldItemName,held!=null&&!held.IsEmpty?held.itemData.Name:"EMPTY HANDS");
        Set(heldItemQuantity,held!=null&&!held.IsEmpty?held.quantity.ToString():"—");
    }
    void EndTurn() { if(input!=null) input.EndPlayerTurn(); }
    static void Set(Text label,string value) { if(label!=null && label.text!=value)label.text=value; }
}
