using UnityEngine;
using UnityEngine.UI;

/// <summary>Editable, texture-free fallback icons for items without an assigned sprite.</summary>
[AddComponentMenu("UI/Survival Item Glyph")]
[RequireComponent(typeof(CanvasRenderer))]
public class ItemGlyphGraphic : MaskableGraphic
{
    public enum Glyph { Handgun, Bandage, Bottle, Key, Material, Armour }
    [SerializeField] private Glyph glyph;
    public void SetItem(ItemData item)
    {
        string label = (item.Name + " " + item.name).ToLowerInvariant();
        Glyph next = item.Type == ItemType.Weapon ? Glyph.Handgun :
            item.Type == ItemType.KeyItem ? Glyph.Key :
            item.Type == ItemType.Equipment ? Glyph.Armour :
            item.Type == ItemType.Material ? Glyph.Material :
            label.Contains("bandage") || label.Contains("绷带") ? Glyph.Bandage : Glyph.Bottle;
        if (glyph != next) { glyph = next; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        switch (glyph)
        {
            case Glyph.Handgun:
                Quad(vh, .12f,.55f,.85f,.75f); Quad(vh,.45f,.46f,.72f,.59f);
                Poly(vh, new Vector2(.27f,.6f),new Vector2(.49f,.6f),new Vector2(.38f,.16f),new Vector2(.17f,.16f)); break;
            case Glyph.Bandage:
                Poly(vh,new Vector2(.12f,.73f),new Vector2(.26f,.88f),new Vector2(.88f,.27f),new Vector2(.73f,.12f));
                Quad(vh,.43f,.43f,.57f,.57f, new Color(.3f,.4f,.37f,1)); break;
            case Glyph.Bottle:
                Quad(vh,.4f,.68f,.6f,.9f); Quad(vh,.3f,.12f,.7f,.65f);
                Poly(vh,new Vector2(.3f,.65f),new Vector2(.4f,.76f),new Vector2(.6f,.76f),new Vector2(.7f,.65f)); break;
            case Glyph.Key:
                Quad(vh,.42f,.4f,.5f,.8f); Quad(vh,.45f,.4f,.85f,.48f);
                Quad(vh,.72f,.24f,.8f,.48f); Quad(vh,.58f,.31f,.65f,.46f); break;
            case Glyph.Armour:
                Poly(vh,new Vector2(.23f,.8f),new Vector2(.41f,.88f),new Vector2(.5f,.7f),new Vector2(.5f,.12f));
                Poly(vh,new Vector2(.5f,.7f),new Vector2(.59f,.88f),new Vector2(.77f,.8f),new Vector2(.5f,.12f)); break;
            default:
                Quad(vh,.23f,.15f,.77f,.83f); Quad(vh,.28f,.62f,.72f,.67f,new Color(.3f,.4f,.37f,1)); break;
        }
    }

    void Quad(VertexHelper vh,float x,float y,float right,float top,Color? tint=null)
    { Poly(vh,new Vector2(x,y),new Vector2(right,y),new Vector2(right,top),new Vector2(x,top),tint); }
    void Poly(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color? tint=null)
    {
        var rect=GetPixelAdjustedRect(); int start=vh.currentVertCount;
        foreach(var p in new[]{a,b,c,d})
            vh.AddVert(new Vector3(rect.x+p.x*rect.width,rect.y+p.y*rect.height),tint??color,Vector2.zero);
        vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
    }
}
